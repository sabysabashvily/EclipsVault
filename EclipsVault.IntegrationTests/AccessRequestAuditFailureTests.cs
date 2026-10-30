using System.Net;
using EclipsVault.Core.Application.AccessRequests;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditFailureCollection.Name)]
public sealed class AccessRequestAuditFailureTests(AuditFailureFixture failure)
{
    [Theory]
    [InlineData("Approve", false, "152")]
    [InlineData("Reject", false, "153")]
    [InlineData("Cancel", false, "154")]
    [InlineData("Approve", true, "155")]
    public async Task A_failed_audit_leaves_the_request_share_trail_and_decision_email_unchanged(string action, bool replaceExpired, string addressSuffix)
    {
        var vault = failure.Vault;
        var reviewer = await vault.AdminClientAsync();
        var username = $"it-request-failure-{addressSuffix}";
        var requester = await vault.SignInNewAccountAsync(username, IPAddress.Parse($"198.51.100.{addressSuffix}"));
        var details = await LegacySharing.CreateSecretAsync(reviewer, $"it-request-audit-{addressSuffix}");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, username);
        var caller = action == "Cancel" ? requester.Client : reviewer;
        var token = await LegacyAccessRequests.TokenAsync(caller);
        var expiredId = await PrepareExpiredShareAsync(replaceExpired, reviewer, details, username);
        var before = await AuditTrail.HeadAsync(vault);
        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyAccessRequests.PostAsync(caller, action, token,
                pending.Id);
            Assert.Equal("/Home/Error?code=503", response.Headers.Location?.OriginalString);
            using var scope = vault.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var kept = await db.AccessRequests.SingleAsync(request => request.SecretId == pending.SecretId);
            Assert.Equal(pending.Status, kept.Status);
            Assert.Null(kept.DecidedAtUtc);
            Assert.Null(kept.DecidedBy);
            Assert.Null(kept.DecisionNote);
            Assert.Equal(expiredId, await db.SecretGrants.Where(share => share.SecretId == pending.SecretId).Select(share => (Guid?)share.Id).SingleOrDefaultAsync());
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
            Assert.False(await db.EmailLogs.AnyAsync(email => email.Body.Contains(pending.SecretName)));
            Assert.False(await vault.Mailbox!.HasDecisionForAsync($"{username}@example.test"));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Filing_with_a_failed_audit_creates_no_request_through_Legacy_or_the_production_service()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var requester = await vault.SignInNewAccountAsync("it-request-file-failure", IPAddress.Parse("198.51.100.151"));
        var account = await ProfileHttp.AccountAsync(vault, "it-request-file-failure");
        var details = await LegacySharing.CreateSecretAsync(admin, "it-request-file-failure-secret");
        var secretId = LegacySharing.SecretIdOf(details);
        var token = await LegacyAccessRequests.TokenAsync(requester.Client);
        var before = await AuditTrail.HeadAsync(vault);
        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyAccessRequests.PostAsync(requester.Client, "Create", token, secretId);
            Assert.Equal("/Home/Error?code=503", response.Headers.Location?.OriginalString);
            using var scope = vault.Services.CreateScope();
            // Legacy reads metadata before filing. Reach the actual filing save as well, through
            // the unchanged Production container, so the rollback itself is tested.
            var requests = scope.ServiceProvider.GetRequiredService<IAccessRequestService>();
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => requests.CreateAsync(secretId, "it-request-file-failure-secret",
                "GLOBAL", account.Id, account.Username, "Need deployment access", null, default));
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            Assert.False(await db.AccessRequests.AnyAsync(request => request.SecretId == secretId));
            Assert.False(await db.SecretGrants.AnyAsync(share => share.SecretId == secretId));
            Assert.False(await db.EmailLogs.AnyAsync(email => email.Body.Contains("it-request-file-failure-secret")));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
            Assert.False(await vault.Mailbox!.HasDecisionForAsync(account.Email));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    private async Task<Guid?> PrepareExpiredShareAsync(bool replaceExpired, HttpClient reviewer, string details, string username)
    {
        if (!replaceExpired)
        {
            return null;
        }
        await LegacySharing.ShareAsync(reviewer, details, username);
        var expired = await LegacySharing.ShareOfAsync(failure.Vault, details);
        await LegacySharing.ExpireShareAsync(failure.Vault, expired.Id);
        return expired.Id;
    }
}
