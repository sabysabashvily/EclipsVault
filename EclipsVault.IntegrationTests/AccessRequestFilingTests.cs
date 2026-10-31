using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Filing an access request from Legacy's Denied page. Every secret is Confidential in GLOBAL, and every
/// requester signs in from an untrusted address, so the network rule denies them as well.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class AccessRequestFilingTests(VaultFixture vault)
{
    private const string HelpText =
        "Ask a reviewer, an administrator or a member of this secret's project who can open it, to share it with you.";

    [Fact]
    public async Task The_denial_reasons_a_reviewer_reads_are_the_servers_and_never_the_posted_ones()
    {
        var requester = await vault.SignInNewAccountAsync(
            "it-filing-reasons", IPAddress.Parse("203.0.113.210"), ClearanceLevel.Elevated, "IT-FILING");
        var secretId = await ConfidentialSecretAsync("it-filing-reasons-secret");

        await LegacyAccessRequests.PostFormAsync(requester.Client, "Create", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await LegacyAccessRequests.TokenAsync(requester.Client),
            ["secretId"] = secretId.ToString(),
            ["reason"] = "Need this for deployment",
            ["deniedReasons"] = "Pre-approved by security",
        });

        using var scope = vault.Services.CreateScope();
        var request = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.AsNoTracking()
            .SingleAsync(request => request.SecretId == secretId);
        Assert.Equal(
            "Subject project 'IT-FILING' does not match resource project 'GLOBAL'.\n"
            + "Source address '203.0.113.210' is outside the trusted network ranges.",
            request.DeniedReasons);
    }

    [Theory]
    [InlineData("under-cleared", ClearanceLevel.Standard, "IT-FILING", "203.0.113.211",
        "A share would not help: your clearance is below this secret's sensitivity.")]
    [InlineData("project-member", ClearanceLevel.Elevated, "GLOBAL", "203.0.113.212",
        "A share would not help: your project, clearance or an active share already covers this secret.")]
    [InlineData("administrator", ClearanceLevel.TopSecret, "IT-FILING", "203.0.113.213",
        "A share would not help: your project, clearance or an active share already covers this secret.")]
    public async Task A_request_a_share_could_not_help_is_refused_with_its_reason_and_records_nothing(
        string role, ClearanceLevel clearance, string project, string address, string refusal)
    {
        var requester = await vault.SignInNewAccountAsync($"it-filing-{role}", IPAddress.Parse(address), clearance, project);
        var secretId = await ConfidentialSecretAsync($"it-filing-secret-{role}");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAccessRequests.PostAsync(
            requester.Client, "Create", await LegacyAccessRequests.TokenAsync(requester.Client), secretId);

        Assert.Equal("/AccessRequests", response.Headers.Location?.OriginalString);
        Assert.Contains(refusal, WebUtility.HtmlDecode(await requester.Client.GetStringAsync("/AccessRequests")));
        Assert.False(await HasRequestAsync(secretId));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_request_from_an_active_share_holder_is_refused_as_already_covered()
    {
        var issuer = await vault.SignInNewAccountAsync("it-filing-issuer", IPAddress.Parse("10.99.0.7"), ClearanceLevel.TopSecret);
        var requester = await vault.SignInNewAccountAsync(
            "it-filing-share-holder", IPAddress.Parse("203.0.113.214"), ClearanceLevel.Elevated, "IT-FILING");
        var details = await LegacySharing.CreateSecretAsync(issuer.Client, "it-filing-shared-secret", sensitivity: "Confidential");
        await LegacySharing.ShareAsync(issuer.Client, details, "it-filing-share-holder");
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyAccessRequests.PostAsync(
            requester.Client, "Create", await LegacyAccessRequests.TokenAsync(requester.Client), LegacySharing.SecretIdOf(details));

        Assert.Contains(
            "A share would not help: your project, clearance or an active share already covers this secret.",
            WebUtility.HtmlDecode(await requester.Client.GetStringAsync("/AccessRequests")));
        Assert.False(await HasRequestAsync(LegacySharing.SecretIdOf(details)));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_second_request_while_one_is_pending_is_refused_and_records_nothing()
    {
        var requester = await vault.SignInNewAccountAsync(
            "it-filing-pending", IPAddress.Parse("203.0.113.215"), ClearanceLevel.Elevated, "IT-FILING");
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-filing-pending-secret", sensitivity: "Confidential");
        await LegacyAccessRequests.PendingAsync(vault, details, "it-filing-pending");
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyAccessRequests.PostAsync(
            requester.Client, "Create", await LegacyAccessRequests.TokenAsync(requester.Client), LegacySharing.SecretIdOf(details));

        Assert.Contains("You already have a pending request for this secret.", await requester.Client.GetStringAsync("/AccessRequests"));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Filing_on_a_honey_token_trips_it_and_files_nothing()
    {
        // Tripping blocks the requester's address range and ends their sessions. The range is this test's own.
        var requester = await vault.SignInNewAccountAsync(
            "it-filing-decoy", IPAddress.Parse("100.99.99.10"), ClearanceLevel.Elevated, "IT-FILING");
        var secretId = await ConfidentialSecretAsync("it-filing-decoy-secret");
        using (var scope = vault.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.Where(secret => secret.Id == secretId)
                .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.IsHoneyToken, true));
        }
        var token = await LegacyAccessRequests.TokenAsync(requester.Client);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAccessRequests.PostAsync(requester.Client, "Create", token, secretId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal([AuditAction.HoneyTokenTripped], (await AuditTrail.RowsAfterAsync(vault, before)).Select(row => row.Action));
        Assert.False(await HasRequestAsync(secretId));
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.Client.GetAsync("/AccessRequests")).StatusCode);
        var sameSessionElsewhere = vault.CreateSession(requester.SessionCookie, IPAddress.Parse("100.98.98.10"));
        Assert.Equal("/Account/Login", (await sameSessionElsewhere.Client.GetAsync("/AccessRequests")).Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task The_Denied_page_trips_a_honey_token_instead_of_answering_for_it()
    {
        // Tripping blocks the requester's address range and ends their sessions. The range is this test's own.
        var requester = await vault.SignInNewAccountAsync(
            "it-denied-decoy", IPAddress.Parse("100.99.98.10"), ClearanceLevel.Elevated, "IT-FILING");
        var secretId = await ConfidentialSecretAsync("it-denied-decoy-secret");
        using (var scope = vault.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.Where(secret => secret.Id == secretId)
                .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.IsHoneyToken, true));
        }
        // Kept before the request, whose answer clears the cookie from this browser.
        var sessionCookie = requester.SessionCookie;
        Assert.NotNull(sessionCookie);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await requester.Client.GetAsync($"/Account/Denied?secretId={secretId}");

        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
        Assert.Equal([AuditAction.HoneyTokenTripped], (await AuditTrail.RowsAfterAsync(vault, before)).Select(row => row.Action));
        var sameSessionElsewhere = vault.CreateSession(sessionCookie, IPAddress.Parse("100.98.97.10"));
        Assert.Equal("/Account/Login", (await sameSessionElsewhere.Client.GetAsync("/AccessRequests")).Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task The_Denied_page_offers_the_request_form_when_a_share_would_help()
    {
        var requester = await vault.SignInNewAccountAsync(
            "it-denied-eligible", IPAddress.Parse("203.0.113.216"), ClearanceLevel.Elevated, "IT-FILING");
        var secretId = await ConfidentialSecretAsync("it-denied-eligible-secret");
        var before = await AuditTrail.HeadAsync(vault);

        var page = WebUtility.HtmlDecode(await requester.Client.GetStringAsync($"/Account/Denied?secretId={secretId}"));

        Assert.Contains(HelpText, page);
        Assert.Contains("Submit request", page);
        Assert.DoesNotContain("deniedReasons", page);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Theory]
    [InlineData("under-cleared", ClearanceLevel.Standard, "IT-FILING", "203.0.113.217",
        "A share would not help: your clearance is below this secret's sensitivity.")]
    [InlineData("project-member", ClearanceLevel.Elevated, "GLOBAL", "203.0.113.218",
        "A share would not help: your project, clearance or an active share already covers this secret.")]
    public async Task The_Denied_page_says_why_instead_of_offering_the_form_when_a_share_would_not_help(
        string role, ClearanceLevel clearance, string project, string address, string refusal)
    {
        var requester = await vault.SignInNewAccountAsync($"it-denied-{role}", IPAddress.Parse(address), clearance, project);
        var secretId = await ConfidentialSecretAsync($"it-denied-secret-{role}");
        var before = await AuditTrail.HeadAsync(vault);

        var page = WebUtility.HtmlDecode(await requester.Client.GetStringAsync($"/Account/Denied?secretId={secretId}"));

        Assert.Contains(refusal, page);
        Assert.DoesNotContain("Submit request", page);
        Assert.DoesNotContain(HelpText, page);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task The_Denied_page_says_a_request_is_already_pending_instead_of_offering_the_form()
    {
        var requester = await vault.SignInNewAccountAsync(
            "it-denied-pending", IPAddress.Parse("203.0.113.219"), ClearanceLevel.Elevated, "IT-FILING");
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-denied-pending-secret", sensitivity: "Confidential");
        await LegacyAccessRequests.PendingAsync(vault, details, "it-denied-pending");

        var page = await requester.Client.GetStringAsync($"/Account/Denied?secretId={LegacySharing.SecretIdOf(details)}");

        Assert.Contains("You already have a pending request for this secret.", page);
        Assert.DoesNotContain("Submit request", page);
    }

    private async Task<Guid> ConfidentialSecretAsync(string name)
        => LegacySharing.SecretIdOf(await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), name, sensitivity: "Confidential"));

    private async Task<bool> HasRequestAsync(Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.AnyAsync(request => request.SecretId == secretId);
    }
}
