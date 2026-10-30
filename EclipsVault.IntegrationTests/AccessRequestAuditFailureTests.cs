using System.Net;
using EclipsVault.Core.Application.AccessRequests;
using EclipsVault.Core.Domain.Entities;
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
    [InlineData("Approve", "152")]
    [InlineData("Reject", "153")]
    public async Task A_failed_review_audit_keeps_the_request_share_trail_and_email_unchanged(string action, string addressSuffix)
    {
        var (pending, reviewer, _, _) = await PendingAsync(addressSuffix);
        await AssertRollbackAsync(pending, reviewer, action, expectedShareId: null);
    }

    [Fact]
    public async Task A_failed_withdrawal_audit_keeps_the_request_share_trail_and_email_unchanged()
    {
        var (pending, _, requester, _) = await PendingAsync("154");
        await AssertRollbackAsync(pending, requester.Client, "Cancel", expectedShareId: null);
    }

    [Fact]
    public async Task A_failed_approval_audit_keeps_the_expired_share_request_trail_and_email_unchanged()
    {
        var (pending, reviewer, _, details) = await PendingAsync("155");
        await LegacySharing.ShareAsync(reviewer, details, pending.RequesterUsername);
        var expired = await LegacySharing.ShareOfAsync(failure.Vault, details);
        await LegacySharing.ExpireShareAsync(failure.Vault, expired.Id);
        await AssertRollbackAsync(pending, reviewer, "Approve", expired.Id);
        Assert.False((await LegacySharing.ShareOfAsync(failure.Vault, details)).IsActive(DateTimeOffset.UtcNow));
    }

    private async Task AssertRollbackAsync(AccessRequest pending, HttpClient caller, string action, Guid? expectedShareId)
    {
        var vault = failure.Vault;
        var token = await LegacyAccessRequests.TokenAsync(caller);
        var before = await AuditTrail.HeadAsync(vault);
        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyAccessRequests.PostAsync(caller, action, token, pending.Id);
            Assert.Equal("/Home/Error?code=503", response.Headers.Location?.OriginalString);
            using var scope = vault.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var kept = await db.AccessRequests.SingleAsync(request => request.SecretId == pending.SecretId);
            Assert.Equal(pending.Status, kept.Status);
            Assert.Null(kept.DecidedAtUtc);
            Assert.Null(kept.DecidedBy);
            Assert.Null(kept.DecisionNote);
            Assert.Equal(expectedShareId, await db.SecretGrants.Where(share => share.SecretId == pending.SecretId).Select(share => (Guid?)share.Id).SingleOrDefaultAsync());
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
            Assert.False(await db.EmailLogs.AnyAsync(email => email.Body.Contains(pending.SecretName)));
            Assert.False(await vault.Mailbox!.HasDecisionForAsync($"{pending.RequesterUsername}@example.test"));
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

    private async Task<(AccessRequest Pending, HttpClient Reviewer, VaultSession Requester, string Details)> PendingAsync(string addressSuffix)
    {
        var reviewer = await failure.Vault.AdminClientAsync();
        var username = $"it-request-failure-{addressSuffix}";
        var requester = await failure.Vault.SignInNewAccountAsync(username, IPAddress.Parse($"198.51.100.{addressSuffix}"));
        var details = await LegacySharing.CreateSecretAsync(reviewer, $"it-request-audit-{addressSuffix}");
        var pending = await LegacyAccessRequests.PendingAsync(failure.Vault, details, username);
        return (pending, reviewer, requester, details);
    }
}
