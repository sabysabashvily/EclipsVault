using System.Net;
using System.Net.Http.Json;
using System.Text;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class AccessRequestAuditTests(VaultFixture vault)
{
    private const string ExpiryOutOfRange = "Grant expiry must be between 0 and 3650 days.";
    private const string Unreadable = "The request could not be read.";

    [Theory]
    [InlineData("approve", AccessRequestStatus.Approved, AuditAction.AccessRequestApproved,
        "Access to 'it-request-secret-161' approved for it-request-161", new[] { AuditAction.AccessRequestApproved, AuditAction.SecretShared }, "161")]
    [InlineData("reject", AccessRequestStatus.Rejected, AuditAction.AccessRequestRejected,
        "Access to 'it-request-secret-162' rejected for it-request-162", new[] { AuditAction.AccessRequestRejected }, "162")]
    public async Task A_review_records_its_exact_audit_text(
        string action, AccessRequestStatus status, AuditAction auditAction, string expectedText, AuditAction[] expectedActions, string addressSuffix)
    {
        var reviewer = await vault.AdminClientAsync();
        var username = $"it-request-{addressSuffix}";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse($"198.51.100.{addressSuffix}"), project: "IT-REQUESTERS");
        var name = $"it-request-secret-{addressSuffix}";
        var details = await LegacySharing.CreateSecretAsync(reviewer, name);
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, username);
        var token = await UiApiRequest.AntiforgeryTokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);

        // Reject ignores the body, so both reviews send the one approval needs.
        var response = await AccessRequestsApi.DecideAsync(reviewer, token, pending.Id, action, JsonContent.Create(new { expiresInDays = 0 }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal(expectedActions, rows.Select(row => row.Action));
        var decision = Assert.Single(rows, row => row.Action == auditAction);
        Assert.Equal("Secret", decision.ResourceType);
        Assert.Equal(pending.SecretId, decision.ResourceId);
        Assert.Equal(name, decision.ResourceName);
        Assert.Equal(expectedText, decision.Details);
        Assert.Equal(VaultFixture.AdminUsername, decision.Username);
        using var scope = vault.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.SingleAsync(request => request.Id == pending.Id);
        Assert.Equal(status, stored.Status);
        Assert.NotNull(stored.DecidedAtUtc);
        Assert.Equal(VaultFixture.AdminUsername, stored.DecidedBy);
    }

    [Fact]
    public async Task A_withdrawal_records_its_exact_audit_text()
    {
        var reviewer = await vault.AdminClientAsync();
        var requester = await vault.SignInNewAccountAsync("it-request-163", IPAddress.Parse("198.51.100.163"));
        var details = await LegacySharing.CreateSecretAsync(reviewer, "it-request-secret-163");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, "it-request-163");
        var token = await UiApiRequest.AntiforgeryTokenAsync(requester.Client);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.DecideAsync(requester.Client, token, pending.Id, "withdraw");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.AccessRequestCancelled, row.Action);
        Assert.Equal("Secret", row.ResourceType);
        Assert.Equal(pending.SecretId, row.ResourceId);
        Assert.Equal("it-request-secret-163", row.ResourceName);
        Assert.Equal("Request for 'it-request-secret-163' withdrawn", row.Details);
        Assert.Equal("it-request-163", row.Username);
    }

    [Fact]
    public async Task An_approval_carrying_a_decision_note_is_refused_and_the_stored_note_stays()
    {
        var reviewer = await vault.AdminClientAsync();
        var requester = await vault.SignInNewAccountAsync("it-request-note-174", IPAddress.Parse("198.51.100.174"), project: "IT-REQUESTERS");
        var pending = await PendingWithStoredNoteAsync(reviewer, "it-request-note-174", "it-request-note-secret-174");
        var token = await UiApiRequest.AntiforgeryTokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.DecideAsync(reviewer, token, pending.Id, "approve",
            JsonContent.Create(new { expiresInDays = 0, note = "A forged decision note" }));

        var problem = await ProfileApi.AssertProblemAsync(response, 400, "validation_failed");
        Assert.Equal(Unreadable, problem.GetProperty("title").GetString());
        Assert.Equal(AccessRequestStatus.Pending, await StatusOfAsync(pending.Id));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal("A previously stored note", await DisplayedNoteAsync(requester));
    }

    [Fact]
    public async Task A_rejection_ignores_a_posted_note_and_the_stored_note_stays_displayed()
    {
        var reviewer = await vault.AdminClientAsync();
        var requester = await vault.SignInNewAccountAsync("it-request-note-175", IPAddress.Parse("198.51.100.175"), project: "IT-REQUESTERS");
        var pending = await PendingWithStoredNoteAsync(reviewer, "it-request-note-175", "it-request-note-secret-175");
        var token = await UiApiRequest.AntiforgeryTokenAsync(reviewer);

        var response = await AccessRequestsApi.DecideAsync(reviewer, token, pending.Id, "reject",
            JsonContent.Create(new { note = "A forged decision note" }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("A previously stored note", await DisplayedNoteAsync(requester));
        using var check = vault.Services.CreateScope();
        var email = await check.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailLogs
            .SingleAsync(log => log.Body.Contains("it-request-note-secret-175"));
        Assert.DoesNotContain("A forged decision note", email.Body);
    }

    [Fact]
    public async Task Filing_through_Legacy_records_only_the_access_requested_row_with_its_existing_text()
    {
        var reviewer = await vault.AdminClientAsync();
        var requester = await vault.SignInNewAccountAsync("it-request-file", IPAddress.Parse("198.51.100.164"), project: "IT-REQUESTERS");
        var details = await LegacySharing.CreateSecretAsync(reviewer, "it-request-file-secret");
        var token = await LegacyAccessRequests.TokenAsync(requester.Client);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAccessRequests.FileAsync(requester.Client, token, LegacySharing.SecretIdOf(details));

        Assert.Equal("/AccessRequests?filed=1", response.Headers.Location?.OriginalString);
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.AccessRequested, row.Action);
        Assert.Equal("Secret", row.ResourceType);
        Assert.Equal(LegacySharing.SecretIdOf(details), row.ResourceId);
        Assert.Equal("it-request-file-secret", row.ResourceName);
        Assert.Equal("Access to 'it-request-file-secret' requested", row.Details);
        Assert.Equal("it-request-file", row.Username);
    }

    [Fact]
    public async Task Approval_uses_the_requester_account_and_session_reviewer_even_when_the_requester_username_is_reused()
    {
        var admin = await vault.AdminClientAsync();
        var reviewer = await vault.SignInNewAccountAsync("it-request-issuer", IPAddress.Parse("198.51.100.165"));
        await vault.SignInNewAccountAsync("it-request-original", IPAddress.Parse("198.51.100.166"), project: "IT-REQUESTERS");
        var details = await LegacySharing.CreateSecretAsync(admin, "it-request-account-identity");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, "it-request-original");
        using (var scope = vault.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            // A former display snapshot can be provisioned again; it must not select the recipient.
            await db.Users.Where(user => user.Id == pending.RequesterUserId)
                .ExecuteUpdateAsync(set => set.SetProperty(user => user.Username, "it-request-renamed"));
        }
        await vault.SignInNewAccountAsync("it-request-original", IPAddress.Parse("198.51.100.167"));
        var token = await UiApiRequest.AntiforgeryTokenAsync(reviewer.Client);
        var before = await AuditTrail.HeadAsync(vault);

        Assert.Equal(HttpStatusCode.NoContent, (await AccessRequestsApi.DecideAsync(
            reviewer.Client, token, pending.Id, "approve", JsonContent.Create(new { expiresInDays = 0 }))).StatusCode);

        var share = await LegacySharing.ShareOfAsync(vault, details);
        Assert.Equal(pending.RequesterUserId, share.GranteeUserId);
        Assert.Equal("it-request-renamed", share.GranteeUsername);
        var issuer = await ProfileHttp.AccountAsync(vault, "it-request-issuer");
        Assert.Equal(issuer.Id, share.IssuerUserId);
        Assert.Equal("it-request-issuer", share.GrantedBy);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal([AuditAction.AccessRequestApproved, AuditAction.SecretShared], rows.Select(row => row.Action));
        Assert.Equal("Shared with 'it-request-renamed'", rows[1].Details);
        Assert.Equal("Access to 'it-request-account-identity' approved for it-request-original", rows[0].Details);
        Assert.Contains("it-request-account-identity", await reviewer.Client.GetStringAsync("/api/ui/shared-by-me"));
        using var check = vault.Services.CreateScope();
        var email = await check.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailLogs
            .SingleAsync(email => email.EventType == "AccessRequestApproved" && email.Body.Contains("it-request-account-identity"));
        Assert.Contains("was approved by it-request-issuer.", email.Body);
        Assert.DoesNotContain("Reviewer note", email.Body);
    }

    [Fact]
    public async Task Approval_is_refused_with_its_reason_when_a_share_could_no_longer_help()
    {
        var reviewer = await vault.AdminClientAsync();
        await vault.SignInNewAccountAsync("it-request-existing-168", IPAddress.Parse("198.51.100.168"), project: "IT-REQUESTERS");
        var details = await LegacySharing.CreateSecretAsync(reviewer, "it-request-existing-168");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, "it-request-existing-168");
        await LegacySharing.ShareAsync(reviewer, details, "it-request-existing-168");
        var token = await UiApiRequest.AntiforgeryTokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.DecideAsync(reviewer, token, pending.Id, "approve", JsonContent.Create(new { expiresInDays = 0 }));

        var problem = await ProfileApi.AssertProblemAsync(response, 409, "request_ineligible");
        Assert.Equal(
            "A share would not help: the requester's project, clearance or an active share already covers this secret.",
            problem.GetProperty("title").GetString());
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal(AccessRequestStatus.Pending, await StatusOfAsync(pending.Id));
    }

    [Fact]
    public async Task Rejection_decides_a_request_a_share_could_no_longer_help()
    {
        var reviewer = await vault.AdminClientAsync();
        await vault.SignInNewAccountAsync("it-request-existing-241", IPAddress.Parse("198.51.100.241"), project: "IT-REQUESTERS");
        var details = await LegacySharing.CreateSecretAsync(reviewer, "it-request-existing-241");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, "it-request-existing-241");
        await LegacySharing.ShareAsync(reviewer, details, "it-request-existing-241");
        var token = await UiApiRequest.AntiforgeryTokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);

        Assert.Equal(HttpStatusCode.NoContent, (await AccessRequestsApi.DecideAsync(reviewer, token, pending.Id, "reject")).StatusCode);

        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.AccessRequestRejected, row.Action);
        Assert.Equal(AccessRequestStatus.Rejected, await StatusOfAsync(pending.Id));
    }

    [Fact]
    public async Task Approval_is_refused_when_the_secret_has_expired()
    {
        var reviewer = await vault.AdminClientAsync();
        await vault.SignInNewAccountAsync("it-request-expired-secret", IPAddress.Parse("198.51.100.242"), project: "IT-REQUESTERS");
        var details = await LegacySharing.CreateSecretAsync(reviewer, "it-request-expired-secret");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, "it-request-expired-secret");
        using (var scope = vault.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.Where(secret => secret.Id == pending.SecretId)
                .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        var token = await UiApiRequest.AntiforgeryTokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.DecideAsync(reviewer, token, pending.Id, "approve", JsonContent.Create(new { expiresInDays = 0 }));

        var problem = await ProfileApi.AssertProblemAsync(response, 409, "request_ineligible");
        Assert.Equal("That secret no longer exists.", problem.GetProperty("title").GetString());
        Assert.False(await LegacySharing.HasAnyShareAsync(vault, details));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal(AccessRequestStatus.Pending, await StatusOfAsync(pending.Id));
    }

    [Fact]
    public async Task Approval_replaces_an_expired_share_with_one_share_audit_row()
    {
        var reviewer = await vault.AdminClientAsync();
        await vault.SignInNewAccountAsync("it-request-existing-169", IPAddress.Parse("198.51.100.169"), project: "IT-REQUESTERS");
        var details = await LegacySharing.CreateSecretAsync(reviewer, "it-request-existing-169");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, "it-request-existing-169");
        await LegacySharing.ShareAsync(reviewer, details, "it-request-existing-169");
        var existing = await LegacySharing.ShareOfAsync(vault, details);
        await LegacySharing.ExpireShareAsync(vault, existing.Id);
        var token = await UiApiRequest.AntiforgeryTokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);

        Assert.Equal(HttpStatusCode.NoContent, (await AccessRequestsApi.DecideAsync(
            reviewer, token, pending.Id, "approve", JsonContent.Create(new { expiresInDays = 0 }))).StatusCode);

        Assert.NotEqual(existing.Id, (await LegacySharing.ShareOfAsync(vault, details)).Id);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal([AuditAction.AccessRequestApproved, AuditAction.SecretShared], rows.Select(row => row.Action));
        Assert.Equal("Shared with 'it-request-existing-169', replacing an expired share", rows[1].Details);
    }

    [Theory]
    [InlineData("""{"expiresInDays":-1}""", ExpiryOutOfRange, "170")]
    [InlineData("""{"expiresInDays":3651}""", ExpiryOutOfRange, "171")]
    [InlineData("""{"expiresInDays":null}""", ExpiryOutOfRange, "176")]
    [InlineData("{}", ExpiryOutOfRange, "177")]
    [InlineData("""{"expiresInDays":2147483648}""", Unreadable, "172")]
    [InlineData("""{"expiresInDays":"mistyped"}""", Unreadable, "173")]
    public async Task An_invalid_expiry_is_refused_without_deciding_or_sharing(string body, string title, string addressSuffix)
    {
        var reviewer = await vault.AdminClientAsync();
        var username = $"it-request-invalid-{addressSuffix}";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse($"198.51.100.{addressSuffix}"), project: "IT-REQUESTERS");
        var details = await LegacySharing.CreateSecretAsync(reviewer, $"it-request-invalid-{addressSuffix}");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, username);
        var token = await UiApiRequest.AntiforgeryTokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AccessRequestsApi.DecideAsync(reviewer, token, pending.Id, "approve",
            new StringContent(body, Encoding.UTF8, "application/json"));

        var problem = await ProfileApi.AssertProblemAsync(response, 400, "validation_failed");
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.False(await LegacySharing.HasAnyShareAsync(vault, details));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal(AccessRequestStatus.Pending, await StatusOfAsync(pending.Id));
    }

    private async Task<AccessRequest> PendingWithStoredNoteAsync(HttpClient admin, string requester, string secretName)
    {
        var details = await LegacySharing.CreateSecretAsync(admin, secretName);
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, requester);
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.Where(r => r.Id == pending.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.DecisionNote, "A previously stored note"));
        return pending;
    }

    private static async Task<string?> DisplayedNoteAsync(VaultSession requester)
        => Assert.Single((await AccessRequestsApi.PageAsync(requester.Client)).GetProperty("mine").EnumerateArray())
            .GetProperty("decisionNote").GetString();

    private async Task<AccessRequestStatus> StatusOfAsync(Guid requestId)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.AsNoTracking()
            .Where(request => request.Id == requestId).Select(request => request.Status).SingleAsync();
    }
}
