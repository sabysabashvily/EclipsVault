using System.Data.Common;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A share save answers that the recipient already has a share only when the database refused it
/// for that reason and the other share can be seen. Every other failure answers that the audit trail
/// is unavailable, even when another share was saved meanwhile.
/// </summary>
public abstract class ShareSaveFailureTests(ShareDatabases databases)
{
    [Fact]
    public async Task A_save_failing_for_another_reason_answers_the_audit_failure_though_a_share_now_exists()
    {
        var (issuer, recipient, secret) = await databases.CreateSecretToShareAsync("it-share-other-failure");
        var competitor = new SaveOtherShareBeforeTheInsert(() => databases.SaveShareDirectlyAsync(secret, issuer, recipient), Then.FailTheInsert);
        await using var request = databases.NewRequest(issuer, competitor);

        await Assert.ThrowsAsync<AuditWriteFailedException>(
            () => request.Shares.GrantAsync(secret.Id, recipient.Username, ttlDays: null, default));

        await using var check = databases.NewRequest(issuer);
        var stored = await check.Context.SecretGrants.SingleAsync(share => share.SecretId == secret.Id);
        Assert.Equal(competitor.SavedShareId, stored.Id);
    }

    [Fact]
    public async Task A_share_refused_by_the_index_answers_the_audit_failure_when_the_database_then_goes_away()
    {
        var (issuer, recipient, secret) = await databases.CreateSecretToShareAsync("it-share-database-gone");
        var competitor = new SaveOtherShareBeforeTheInsert(() => databases.SaveShareDirectlyAsync(secret, issuer, recipient), Then.FailEveryLaterStatement);
        await using var request = databases.NewRequest(issuer, competitor);

        await Assert.ThrowsAsync<AuditWriteFailedException>(
            () => request.Shares.GrantAsync(secret.Id, recipient.Username, ttlDays: null, default));

        await using var check = databases.NewRequest(issuer);
        var stored = await check.Context.SecretGrants.SingleAsync(share => share.SecretId == secret.Id);
        Assert.Equal(competitor.SavedShareId, stored.Id);
    }

    private enum Then
    {
        FailTheInsert,
        FailEveryLaterStatement,
    }

    /// <summary>
    /// Saves another share for the recipient just before the request's share insert runs, then fails
    /// either that insert, or every statement after it as a database that has gone away.
    /// </summary>
    private sealed class SaveOtherShareBeforeTheInsert(Func<Task<Guid>> saveOtherShare, Then then) : DbCommandInterceptor
    {
        private bool _insertReached;

        public Guid SavedShareId { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (_insertReached && then == Then.FailEveryLaterStatement)
            {
                throw new InvalidOperationException("The database is unreachable.");
            }

            if (!_insertReached && command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("SecretGrants", StringComparison.Ordinal))
            {
                _insertReached = true;
                SavedShareId = await saveOtherShare();
                if (then == Then.FailTheInsert)
                {
                    throw new InvalidOperationException("The audit store is unreachable.");
                }
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

[Collection(SqlServerShareCollection.Name)]
public sealed class SqlServerShareSaveFailureTests(SqlServerShareDatabases databases)
    : ShareSaveFailureTests(databases);

[Collection(PostgresShareCollection.Name)]
public sealed class PostgresShareSaveFailureTests(PostgresShareDatabases databases)
    : ShareSaveFailureTests(databases);
