using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Denied page's questions to the UI API: whether to offer the request form, and filing. Every secret
/// is Confidential in GLOBAL, and every requester signs in from an untrusted address, so the network rule
/// denies them as well.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class AccessRequestFilingTests(VaultFixture vault)
{
    [Fact]
    public async Task The_denial_reasons_a_reviewer_reads_are_the_servers()
    {
        var requester = await vault.SignInNewAccountAsync(
            "it-filing-reasons", IPAddress.Parse("203.0.113.210"), ClearanceLevel.Elevated, "IT-FILING");
        var secretId = await ConfidentialSecretAsync("it-filing-reasons-secret");

        Assert.Equal(HttpStatusCode.NoContent, (await AccessRequestsApi.FileAsync(requester.Client, secretId)).StatusCode);

        using var scope = vault.Services.CreateScope();
        var request = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.AsNoTracking()
            .SingleAsync(request => request.SecretId == secretId);
        Assert.Equal(
            "Subject project 'IT-FILING' does not match resource project 'GLOBAL'.\n"
            + "Source address '203.0.113.210' is outside the trusted network ranges.",
            request.DeniedReasons);
    }

    [Fact]
    public async Task A_filing_that_brings_its_own_denial_reasons_is_refused_and_files_nothing()
    {
        var requester = await vault.SignInNewAccountAsync(
            "it-filing-posted-reasons", IPAddress.Parse("203.0.113.240"), ClearanceLevel.Elevated, "IT-FILING");
        var secretId = await ConfidentialSecretAsync("it-filing-posted-reasons-secret");
        var token = await UiApiRequest.AntiforgeryTokenAsync(requester.Client);

        var response = await UiApiRequest.SendWithTokenAsync(requester.Client, token, HttpMethod.Post, AccessRequestsApi.Path,
            JsonContent.Create(new { secretId, reason = "Need this for deployment", deniedReasons = "Pre-approved by security" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await HasRequestAsync(secretId));
    }

    [Theory]
    [InlineData("under-cleared", ClearanceLevel.Standard, "IT-FILING", "203.0.113.211", "share_cannot_help",
        "A share would not help: your clearance is below this secret's sensitivity.")]
    [InlineData("project-member", ClearanceLevel.Elevated, "GLOBAL", "203.0.113.212", "already_able_to_open",
        "A share would not help: your project, clearance or an active share already covers this secret.")]
    [InlineData("administrator", ClearanceLevel.TopSecret, "IT-FILING", "203.0.113.213", "already_able_to_open",
        "A share would not help: your project, clearance or an active share already covers this secret.")]
    public async Task A_request_a_share_could_not_help_is_refused_with_its_reason_and_records_nothing(
        string role, ClearanceLevel clearance, string project, string address, string code, string title)
    {
        var requester = await vault.SignInNewAccountAsync($"it-filing-{role}", IPAddress.Parse(address), clearance, project);
        var secretId = await ConfidentialSecretAsync($"it-filing-secret-{role}");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.FileAsync(requester.Client, secretId);

        var problem = await SecretsApi.AssertProblemAsync(response, 409, code);
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.False(await HasRequestAsync(secretId));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_request_from_an_active_share_holder_is_refused_as_already_covered()
    {
        var issuer = await vault.SignInNewAccountAsync("it-filing-issuer", IPAddress.Parse("10.99.0.7"), ClearanceLevel.TopSecret);
        var requester = await vault.SignInNewAccountAsync(
            "it-filing-share-holder", IPAddress.Parse("203.0.113.214"), ClearanceLevel.Elevated, "IT-FILING");
        var details = await SecretSharing.CreateSecretAsync(issuer.Client, "it-filing-shared-secret", sensitivity: "Confidential");
        await SecretDetailsApi.ShareAsync(issuer.Client, details, "it-filing-share-holder");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.FileAsync(requester.Client, SecretSharing.SecretIdOf(details));

        await SecretsApi.AssertProblemAsync(response, 409, "already_able_to_open");
        Assert.False(await HasRequestAsync(SecretSharing.SecretIdOf(details)));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_second_request_while_one_is_pending_is_refused_and_records_nothing()
    {
        var requester = await vault.SignInNewAccountAsync(
            "it-filing-pending", IPAddress.Parse("203.0.113.215"), ClearanceLevel.Elevated, "IT-FILING");
        var details = await SecretSharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-filing-pending-secret", sensitivity: "Confidential");
        await AccessRequestsApi.PendingAsync(vault, details, "it-filing-pending");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.FileAsync(requester.Client, SecretSharing.SecretIdOf(details));

        var problem = await SecretsApi.AssertProblemAsync(response, 409, "already_pending");
        Assert.Equal("You already have a pending request for this secret.", problem.GetProperty("title").GetString());
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Filing_without_the_antiforgery_header_files_nothing()
    {
        var requester = await vault.SignInNewAccountAsync(
            "it-filing-csrf", IPAddress.Parse("203.0.113.241"), ClearanceLevel.Elevated, "IT-FILING");
        var secretId = await ConfidentialSecretAsync("it-filing-csrf-secret");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await requester.Client.PostAsync(AccessRequestsApi.Path, JsonContent.Create(new { secretId, reason = "Need this for deployment" }));

        await UiApiFailureContractTests.AssertProblemAsync(response, 400, "antiforgery_rejected");
        Assert.False(await HasRequestAsync(secretId));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Filing_on_a_honey_token_trips_it_and_files_nothing()
    {
        // Tripping blocks the requester's address range and ends their sessions. The range is this test's own.
        var requester = await vault.SignInNewAccountAsync(
            "it-filing-decoy", IPAddress.Parse("100.99.99.10"), ClearanceLevel.Elevated, "IT-FILING");
        var secretId = await DecoyAsync("it-filing-decoy-secret");
        var token = await UiApiRequest.AntiforgeryTokenAsync(requester.Client);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.FileWithTokenAsync(requester.Client, token, secretId);

        await SecretsApi.AssertProblemAsync(response, 404, "not_found");
        Assert.Equal([AuditAction.HoneyTokenTripped], (await AuditTrail.RowsAfterAsync(vault, before)).Select(row => row.Action));
        Assert.False(await HasRequestAsync(secretId));
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.Client.GetAsync("/AccessRequests")).StatusCode);
        var sameSessionElsewhere = vault.CreateSession(requester.SessionCookie, IPAddress.Parse("100.98.98.10"));
        Assert.Equal("/Account/Login", (await sameSessionElsewhere.Client.GetAsync("/AccessRequests")).Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Asking_about_a_honey_token_trips_it_and_answers_not_found()
    {
        // Tripping blocks the requester's address range and ends their sessions. The range is this test's own.
        var requester = await vault.SignInNewAccountAsync(
            "it-denied-decoy", IPAddress.Parse("100.99.98.10"), ClearanceLevel.Elevated, "IT-FILING");
        var secretId = await DecoyAsync("it-denied-decoy-secret");
        var sessionCookie = requester.SessionCookie;
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.EligibilityAsync(requester.Client, secretId);

        await SecretsApi.AssertProblemAsync(response, 404, "not_found");
        Assert.Equal([AuditAction.HoneyTokenTripped], (await AuditTrail.RowsAfterAsync(vault, before)).Select(row => row.Action));
        var sameSessionElsewhere = vault.CreateSession(sessionCookie, IPAddress.Parse("100.98.97.10"));
        Assert.Equal("/Account/Login", (await sameSessionElsewhere.Client.GetAsync("/AccessRequests")).Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task The_form_is_offered_when_a_share_would_help_and_asking_records_nothing()
    {
        var requester = await vault.SignInNewAccountAsync(
            "it-denied-eligible", IPAddress.Parse("203.0.113.216"), ClearanceLevel.Elevated, "IT-FILING");
        var secretId = await ConfidentialSecretAsync("it-denied-eligible-secret");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.EligibilityAsync(requester.Client, secretId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var answer = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["eligible", "message", "refusal"], ProfileApi.NamesOf(answer));
        Assert.True(answer.GetProperty("eligible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("refusal").ValueKind);
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("message").ValueKind);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Theory]
    [InlineData("under-cleared", ClearanceLevel.Standard, "IT-FILING", "203.0.113.217", "share_cannot_help",
        "A share would not help: your clearance is below this secret's sensitivity.")]
    [InlineData("project-member", ClearanceLevel.Elevated, "GLOBAL", "203.0.113.218", "already_able_to_open",
        "A share would not help: your project, clearance or an active share already covers this secret.")]
    public async Task The_page_is_told_why_instead_of_offering_the_form_when_a_share_would_not_help(
        string role, ClearanceLevel clearance, string project, string address, string code, string message)
    {
        var requester = await vault.SignInNewAccountAsync($"it-denied-{role}", IPAddress.Parse(address), clearance, project);
        var secretId = await ConfidentialSecretAsync($"it-denied-secret-{role}");
        var before = await AuditTrail.HeadAsync(vault);

        var answer = await EligibilityOfAsync(requester.Client, secretId);

        Assert.Equal((false, code, message), answer);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task The_page_is_told_a_request_is_already_pending()
    {
        var requester = await vault.SignInNewAccountAsync(
            "it-denied-pending", IPAddress.Parse("203.0.113.219"), ClearanceLevel.Elevated, "IT-FILING");
        var details = await SecretSharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-denied-pending-secret", sensitivity: "Confidential");
        await AccessRequestsApi.PendingAsync(vault, details, "it-denied-pending");

        var answer = await EligibilityOfAsync(requester.Client, SecretSharing.SecretIdOf(details));

        Assert.Equal((false, "already_pending", "You already have a pending request for this secret."), answer);
    }

    [Fact]
    public async Task An_unknown_secret_answers_both_questions_exactly_as_a_decoy()
    {
        // Each decoy trips, ending its requester's sessions and blocking its range, so each has its own.
        var asker = await vault.SignInNewAccountAsync(
            "it-denied-unknown", IPAddress.Parse("203.0.113.242"), ClearanceLevel.Elevated, "IT-FILING");
        var decoyAsker = await vault.SignInNewAccountAsync(
            "it-denied-decoy-ask", IPAddress.Parse("100.99.97.10"), ClearanceLevel.Elevated, "IT-FILING");
        var decoyFiler = await vault.SignInNewAccountAsync(
            "it-denied-decoy-file", IPAddress.Parse("100.99.96.10"), ClearanceLevel.Elevated, "IT-FILING");
        var decoyId = await DecoyAsync("it-denied-decoy-equal-secret");
        var unknownId = Guid.NewGuid();
        var filerToken = await UiApiRequest.AntiforgeryTokenAsync(decoyFiler.Client);

        await AssertSameAnswerAsync(
            await AccessRequestsApi.EligibilityAsync(asker.Client, unknownId),
            await AccessRequestsApi.EligibilityAsync(decoyAsker.Client, decoyId));
        await AssertSameAnswerAsync(
            await AccessRequestsApi.FileAsync(asker.Client, unknownId),
            await AccessRequestsApi.FileWithTokenAsync(decoyFiler.Client, filerToken, decoyId));
    }

    private static async Task AssertSameAnswerAsync(HttpResponseMessage unknown, HttpResponseMessage decoy)
    {
        await SecretsApi.AssertProblemAsync(unknown, 404, "not_found");
        Assert.Equal(decoy.StatusCode, unknown.StatusCode);
        Assert.Equal(decoy.Content.Headers.ContentType, unknown.Content.Headers.ContentType);
        Assert.Equal(await decoy.Content.ReadAsByteArrayAsync(), await unknown.Content.ReadAsByteArrayAsync());
    }

    private async Task<Guid> ConfidentialSecretAsync(string name)
        => SecretSharing.SecretIdOf(await SecretSharing.CreateSecretAsync(await vault.AdminClientAsync(), name, sensitivity: "Confidential"));

    private async Task<Guid> DecoyAsync(string name)
    {
        var secretId = await ConfidentialSecretAsync(name);
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.Where(secret => secret.Id == secretId)
            .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.IsHoneyToken, true));
        return secretId;
    }

    private static async Task<(bool Eligible, string? Refusal, string? Message)> EligibilityOfAsync(HttpClient client, Guid secretId)
    {
        var answer = await (await AccessRequestsApi.EligibilityAsync(client, secretId)).Content.ReadFromJsonAsync<JsonElement>();
        return (answer.GetProperty("eligible").GetBoolean(), answer.GetProperty("refusal").GetString(), answer.GetProperty("message").GetString());
    }

    private async Task<bool> HasRequestAsync(Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.AnyAsync(request => request.SecretId == secretId);
    }
}
