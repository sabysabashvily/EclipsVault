using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Two requests that share one secret with one recipient at the same moment, on each database.
/// Both pass their checks before either saves, so the database decides: one share, and the other
/// request is told the recipient already has one, never that the audit trail failed.
/// </summary>
public abstract class ConcurrentShareTests(ShareDatabases databases)
{
    [Fact]
    public async Task Two_shares_to_one_recipient_at_once_make_one_share_and_a_conflict()
    {
        var (issuer, recipient, secret) = await databases.CreateSecretToShareAsync("it-race-two-shares");
        await using var first = databases.NewRequest(issuer);
        await using var second = databases.NewRequest(issuer);

        Task<SharingException?> firstRefusal, secondRefusal;
        await using (var chain = await databases.HoldAuditChainAsync())
        {
            firstRefusal = RefusalOf(first.Shares.GrantAsync(secret.Id, recipient.Username, ttlDays: null, default));
            secondRefusal = RefusalOf(second.Shares.GrantAsync(secret.Id, recipient.Username, ttlDays: null, default));
            await chain.WaitForQueuedWritersAsync(2);
        }

        var refusals = await Task.WhenAll(firstRefusal, secondRefusal);
        Assert.Single(refusals, refusal => refusal is null);
        Assert.Equal("They already have a share on this secret.", Assert.Single(refusals, refusal => refusal is not null)!.Message);
        await using var check = databases.NewRequest(issuer);
        var share = await check.Context.SecretGrants.SingleAsync(stored => stored.SecretId == secret.Id);
        Assert.Equal(recipient.Id, share.GranteeUserId);
        Assert.Equal([AuditAction.SecretCreated, AuditAction.SecretShared], await ActionsOnAsync(check, secret.Id));
    }

    [Fact]
    public async Task Two_shares_replacing_one_expired_share_at_once_make_one_share_and_a_conflict()
    {
        var (issuer, recipient, secret) = await databases.CreateSecretToShareAsync("it-race-two-replacements");
        var expired = await databases.ShareThatExpiredAsync(secret, issuer, recipient);
        await using var first = databases.NewRequest(issuer);
        await using var second = databases.NewRequest(issuer);

        Task<SharingException?> firstRefusal, secondRefusal;
        await using (var chain = await databases.HoldAuditChainAsync())
        {
            firstRefusal = RefusalOf(first.Shares.GrantAsync(secret.Id, recipient.Username, ttlDays: null, default));
            secondRefusal = RefusalOf(second.Shares.GrantAsync(secret.Id, recipient.Username, ttlDays: null, default));
            await chain.WaitForQueuedWritersAsync(2);
        }

        var refusals = await Task.WhenAll(firstRefusal, secondRefusal);
        Assert.Single(refusals, refusal => refusal is null);
        Assert.Equal("They already have a share on this secret.", Assert.Single(refusals, refusal => refusal is not null)!.Message);
        await using var check = databases.NewRequest(issuer);
        var share = await check.Context.SecretGrants.SingleAsync(stored => stored.SecretId == secret.Id);
        Assert.NotEqual(expired.Id, share.Id);
        Assert.Null(share.ExpiresAtUtc);
        Assert.Equal([AuditAction.SecretCreated, AuditAction.SecretShared, AuditAction.SecretShared], await ActionsOnAsync(check, secret.Id));
    }

    /// <summary>Null when the share was saved, or the refusal the request answered. Any other failure fails the test.</summary>
    private static async Task<SharingException?> RefusalOf(Task share)
    {
        try
        {
            await share;
            return null;
        }
        catch (SharingException refused)
        {
            return refused;
        }
    }

    private static Task<List<AuditAction>> ActionsOnAsync(ShareRequest check, Guid secretId)
        => check.Context.AuditLogs.Where(row => row.ResourceId == secretId).OrderBy(row => row.Sequence).Select(row => row.Action).ToListAsync();
}

[Collection(SqlServerShareCollection.Name)]
public sealed class SqlServerConcurrentShareTests(SqlServerShareDatabases databases)
    : ConcurrentShareTests(databases);

[Collection(PostgresShareCollection.Name)]
public sealed class PostgresConcurrentShareTests(PostgresShareDatabases databases)
    : ConcurrentShareTests(databases);
