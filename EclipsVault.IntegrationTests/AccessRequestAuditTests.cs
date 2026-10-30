using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class AccessRequestAuditTests(VaultFixture vault)
{
    [Theory]
    [InlineData("Approve", AccessRequestStatus.Approved, AuditAction.AccessRequestApproved, "approved", "161")]
    [InlineData("Reject", AccessRequestStatus.Rejected, AuditAction.AccessRequestRejected, "rejected", "162")]
    [InlineData("Cancel", AccessRequestStatus.Cancelled, AuditAction.AccessRequestCancelled, "withdrawn", "163")]
    public async Task A_Legacy_decision_records_its_exact_audit_text_and_ignores_a_posted_note(
        string action, AccessRequestStatus status, AuditAction auditAction, string verb, string addressSuffix)
    {
        var reviewer = await vault.AdminClientAsync();
        var username = $"it-request-{addressSuffix}";
        var requester = await vault.SignInNewAccountAsync(username, IPAddress.Parse($"198.51.100.{addressSuffix}"));
        var name = $"it-request-secret-{addressSuffix}";
        var details = await LegacySharing.CreateSecretAsync(reviewer, name);
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, username);
        var caller = action == "Cancel" ? requester.Client : reviewer;
        var token = await LegacyAccessRequests.TokenAsync(caller);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAccessRequests.PostAsync(caller, action, token, pending.Id);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/AccessRequests", response.Headers.Location?.OriginalString);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        var decision = Assert.Single(rows, row => row.Action == auditAction);
        Assert.Equal("Secret", decision.ResourceType);
        Assert.Equal(pending.SecretId, decision.ResourceId);
        Assert.Equal(name, decision.ResourceName);
        Assert.Equal(action == "Cancel" ? $"Request for '{name}' withdrawn" : $"Access to '{name}' {verb} for {username}", decision.Details);
        Assert.Equal(action == "Cancel" ? username : VaultFixture.AdminUsername, decision.Username);
        Assert.Equal(action == "Approve" ? 2 : 1, rows.Length);
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var stored = await db.AccessRequests.SingleAsync(request => request.Id == pending.Id);
        Assert.Equal(status, stored.Status);
        Assert.Null(stored.DecisionNote);
        Assert.NotNull(stored.DecidedAtUtc);
        Assert.Equal(decision.Username, stored.DecidedBy);
    }

    [Fact]
    public async Task Filing_through_Legacy_records_the_existing_access_requested_text()
    {
        var reviewer = await vault.AdminClientAsync();
        var requester = await vault.SignInNewAccountAsync("it-request-file", IPAddress.Parse("198.51.100.164"));
        var details = await LegacySharing.CreateSecretAsync(reviewer, "it-request-file-secret");
        var token = await LegacyAccessRequests.TokenAsync(requester.Client);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAccessRequests.PostAsync(requester.Client, "Create", token, LegacySharing.SecretIdOf(details));

        Assert.Equal("/AccessRequests", response.Headers.Location?.OriginalString);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal([AuditAction.SecretMetadataViewed, AuditAction.AccessRequested], rows.Select(row => row.Action));
        var row = rows[1];
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
        await vault.SignInNewAccountAsync("it-request-original", IPAddress.Parse("198.51.100.166"));
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
        var token = await LegacyAccessRequests.TokenAsync(reviewer.Client);
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyAccessRequests.PostAsync(reviewer.Client, "Approve", token, pending.Id);

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

    [Theory]
    [InlineData(false, "168")]
    [InlineData(true, "169")]
    public async Task Approval_reuses_an_active_share_or_replaces_an_expired_one_in_its_decision_commit(bool expired, string addressSuffix)
    {
        var reviewer = await vault.AdminClientAsync();
        var username = $"it-request-existing-{addressSuffix}";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse($"198.51.100.{addressSuffix}"));
        var details = await LegacySharing.CreateSecretAsync(reviewer, $"it-request-existing-{addressSuffix}");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, username);
        await LegacySharing.ShareAsync(reviewer, details, username);
        var existing = await LegacySharing.ShareOfAsync(vault, details);
        await ExpireIfRequestedAsync(expired, existing.Id);
        var token = await LegacyAccessRequests.TokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyAccessRequests.PostAsync(reviewer, "Approve", token, pending.Id);

        var share = await LegacySharing.ShareOfAsync(vault, details);
        Assert.Equal(expired, share.Id != existing.Id);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal(expired ? 2 : 1, rows.Length);
        Assert.Equal(AuditAction.AccessRequestApproved, rows[0].Action);
        Assert.Equal(expired ? ["Shared with 'it-request-existing-169', replacing an expired share"] : [],
            rows.Where(row => row.Action == AuditAction.SecretShared).Select(row => row.Details));
    }

    private Task ExpireIfRequestedAsync(bool expired, Guid shareId)
        => expired ? LegacySharing.ExpireShareAsync(vault, shareId) : Task.CompletedTask;

    [Theory]
    [InlineData("-1")]
    [InlineData("3651")]
    [InlineData("2147483648")]
    [InlineData("mistyped")]
    public async Task Invalid_Legacy_expiry_is_refused_without_deciding_or_sharing(string days)
    {
        var reviewer = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(reviewer, $"it-request-invalid-{days}");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, VaultFixture.AdminUsername);
        var token = await LegacyAccessRequests.TokenAsync(reviewer);
        var before = await AuditTrail.HeadAsync(vault);
        await LegacyAccessRequests.PostAsync(reviewer, "Approve", token, pending.Id, days);
        Assert.False(await LegacySharing.HasAnyShareAsync(vault, details));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        using var scope = vault.Services.CreateScope();
        Assert.Equal(AccessRequestStatus.Pending, (await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.SingleAsync(r => r.Id == pending.Id)).Status);
    }
}
