using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EclipsVault.IntegrationTests;

public abstract class ConcurrentAccessRequestTests(ShareDatabases databases)
{
    [Fact]
    public async Task Approval_of_a_snapshot_withdrawn_by_another_request_cannot_commit_a_share()
    {
        var (issuer, recipient, secret) = await databases.CreateSecretToShareAsync("it-request-stale-approval");
        await using var first = databases.NewRequest(issuer);
        var pending = new AccessRequest
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            SecretName = secret.Name,
            ProjectKey = secret.ProjectKey,
            RequesterUserId = recipient.Id,
            RequesterUsername = recipient.Username,
            Reason = "Need deployment access",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await new AccessRequestRepository(first.Context).AddAsync(pending, default);
        await using var second = databases.NewRequest(recipient);
        Assert.True(await second.Requests.CancelAsync(pending.Id, recipient.Id, default));

        Assert.False(await first.Requests.ApproveAsync(pending.Id, 0, default));

        await using var check = databases.NewRequest(issuer);
        Assert.False(await check.Context.SecretGrants.AnyAsync(share => share.SecretId == secret.Id));
        Assert.Equal(AccessRequestStatus.Cancelled, (await check.Context.AccessRequests.SingleAsync(request => request.Id == pending.Id)).Status);
        Assert.Equal([AuditAction.SecretCreated, AuditAction.AccessRequested, AuditAction.AccessRequestCancelled],
            await check.Context.AuditLogs.Where(row => row.ResourceId == secret.Id).OrderBy(row => row.Sequence).Select(row => row.Action).ToArrayAsync());
        Assert.False(await check.Context.EmailLogs.AnyAsync(email => email.Body.Contains(secret.Name)));
    }

    [Theory]
    [InlineData(AccessRequestStatus.Cancelled, 0, "AccessRequestCancelled", "")]
    [InlineData(AccessRequestStatus.Rejected, 0, "AccessRequestRejected", "AccessRequestRejected")]
    [InlineData(AccessRequestStatus.Approved, 1, "SecretShared,AccessRequestApproved", "AccessRequestApproved")]
    public async Task Two_pending_decisions_reach_the_write_together_and_only_one_commits(
        AccessRequestStatus competitor, int alternativeShares, string alternativeActions, string alternativeEmail)
    {
        var (issuer, recipient, secret) = await databases.CreateSecretToShareAsync($"it-request-race-{competitor}");
        var pending = await AddPendingAsync(issuer, recipient, secret);
        await AssertOneWinnerAsync(issuer, secret, pending, competitor,
            new Outcome(competitor, alternativeShares, alternativeActions, alternativeEmail));
    }

    [Theory]
    [InlineData(AccessRequestStatus.Cancelled, 0, "AccessRequestCancelled", "")]
    [InlineData(AccessRequestStatus.Rejected, 0, "AccessRequestRejected", "AccessRequestRejected")]
    [InlineData(AccessRequestStatus.Approved, 1, "SecretShared,AccessRequestApproved", "AccessRequestApproved")]
    public async Task Competing_decisions_with_an_expired_share_commit_only_the_winners_rows(
        AccessRequestStatus competitor, int alternativeShares, string alternativeActions, string alternativeEmail)
    {
        var (issuer, recipient, secret) = await databases.CreateSecretToShareAsync($"it-request-expired-race-{competitor}");
        var pending = await AddPendingAsync(issuer, recipient, secret);
        await databases.ShareThatExpiredAsync(secret, issuer, recipient);
        await AssertOneWinnerAsync(issuer, secret, pending, competitor,
            new Outcome(competitor, alternativeShares, alternativeActions, alternativeEmail));
    }

    [Fact]
    public async Task A_share_saved_while_approval_waits_is_a_share_conflict_and_leaves_the_decision_pending()
    {
        var (issuer, recipient, secret) = await databases.CreateSecretToShareAsync("it-request-share-conflict");
        await using var approval = databases.NewRequest(issuer);
        var pending = new AccessRequest
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            SecretName = secret.Name,
            ProjectKey = secret.ProjectKey,
            RequesterUserId = recipient.Id,
            RequesterUsername = recipient.Username
        };
        await new AccessRequestRepository(approval.Context).AddAsync(pending, default);
        Task<bool> result;
        await using (var chain = await databases.HoldAuditChainAsync())
        {
            result = approval.Requests.ApproveAsync(pending.Id, 0, default);
            await chain.WaitForQueuedWritersAsync(1);
            await databases.SaveShareDirectlyAsync(secret, issuer, recipient);
        }

        var refused = await Assert.ThrowsAsync<SharingException>(() => result);
        Assert.Equal("They already have a share on this secret.", refused.Message);
        await using var check = databases.NewRequest(issuer);
        Assert.Equal(AccessRequestStatus.Pending, (await check.Context.AccessRequests.SingleAsync(r => r.Id == pending.Id)).Status);
        Assert.Equal([AuditAction.SecretCreated, AuditAction.AccessRequested], await check.Context.AuditLogs
            .Where(row => row.ResourceId == secret.Id).OrderBy(row => row.Sequence).Select(row => row.Action).ToArrayAsync());
        Assert.False(await check.Context.EmailLogs.AnyAsync(email => email.Body.Contains(secret.Name)));
    }

    private async Task<AccessRequest> AddPendingAsync(User issuer, User recipient, Secret secret)
    {
        var pending = new AccessRequest
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            SecretName = secret.Name,
            ProjectKey = secret.ProjectKey,
            RequesterUserId = recipient.Id,
            RequesterUsername = recipient.Username,
            Reason = "Need deployment access",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await using var setup = databases.NewRequest(issuer);
        await new AccessRequestRepository(setup.Context).AddAsync(pending, default);
        return pending;
    }

    private async Task AssertOneWinnerAsync(User issuer, Secret secret, AccessRequest pending,
        AccessRequestStatus competingDecision, Outcome alternative)
    {
        await using var first = databases.NewRequest(issuer);
        await using var second = databases.NewRequest(issuer);
        var before = await first.Context.AuditLogs.MaxAsync(row => row.Sequence);
        Task<bool> approval, competitor;
        await using (var chain = await databases.HoldAuditChainAsync())
        {
            approval = first.Requests.ApproveAsync(pending.Id, 0, default);
            competitor = CompeteAsync(second, pending, competingDecision);
            await chain.WaitForQueuedWritersAsync(2);
        }

        Assert.Single(await Task.WhenAll(approval, competitor), won => won);
        await using var check = databases.NewRequest(issuer);
        var status = await check.Context.AccessRequests.Where(r => r.Id == pending.Id).Select(r => r.Status).SingleAsync();
        var actions = await check.Context.AuditLogs.Where(row => row.ResourceId == secret.Id && row.Sequence > before)
            .OrderBy(row => row.Action).Select(row => row.Action).ToArrayAsync();
        var now = DateTimeOffset.UtcNow;
        var shares = await check.Context.SecretGrants.CountAsync(share => share.SecretId == secret.Id
            && (share.ExpiresAtUtc == null || share.ExpiresAtUtc > now));
        var emails = await check.Context.EmailLogs.Where(email => email.Body.Contains(secret.Name)).Select(email => email.EventType).ToArrayAsync();
        Assert.Contains(new Outcome(status, shares, string.Join(",", actions), string.Join(",", emails)), new[]
        {
            new Outcome(AccessRequestStatus.Approved, 1, "SecretShared,AccessRequestApproved", "AccessRequestApproved"),
            alternative,
        });
        Assert.DoesNotContain(await check.Context.EmailLogs.Where(email => email.Body.Contains(secret.Name)).ToArrayAsync(),
            email => email.Body.Contains("Reviewer note", StringComparison.Ordinal));
    }

    private static Task<bool> CompeteAsync(ShareRequest contender, AccessRequest pending, AccessRequestStatus decision)
        => decision switch
        {
            AccessRequestStatus.Approved => contender.Requests.ApproveAsync(pending.Id, 0, default),
            AccessRequestStatus.Rejected => contender.Requests.RejectAsync(pending.Id, default),
            AccessRequestStatus.Cancelled => contender.Requests.CancelAsync(pending.Id, pending.RequesterUserId, default),
            _ => throw new ArgumentOutOfRangeException(nameof(decision)),
        };

    private sealed record Outcome(AccessRequestStatus Status, int Shares, string Actions, string Email);
}

[Collection(SqlServerShareCollection.Name)]
public sealed class SqlServerConcurrentAccessRequestTests(SqlServerShareDatabases databases) : ConcurrentAccessRequestTests(databases);

[Collection(PostgresShareCollection.Name)]
public sealed class PostgresConcurrentAccessRequestTests(PostgresShareDatabases databases) : ConcurrentAccessRequestTests(databases);
