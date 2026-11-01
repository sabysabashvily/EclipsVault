using System.Data.Common;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace EclipsVault.IntegrationTests;

public abstract class AccessRequestSaveFailureTests(ShareDatabases databases)
{
    [Fact]
    public async Task An_unrelated_decision_save_failure_remains_an_audit_failure_and_rolls_back_the_share()
    {
        var (issuer, recipient, secret) = await databases.CreateSecretToShareAsync("it-request-save-failure");
        await using var setup = databases.NewRequest(issuer);
        var pending = new AccessRequest
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            SecretName = secret.Name,
            ProjectKey = secret.ProjectKey,
            RequesterUserId = recipient.Id,
            RequesterUsername = recipient.Username
        };
        await new AccessRequestRepository(setup.Context).AddAsync(pending, default);
        await using var approval = databases.NewRequest(issuer, new RefuseDecisionWrite());

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => approval.Requests.ApproveAsync(pending.Id, 0, default));

        // The same scoped context may save a notification later. It must not resurrect the failure.
        await approval.Context.SaveChangesAsync();
        await using var check = databases.NewRequest(issuer);
        Assert.Equal(AccessRequestStatus.Pending, (await check.Context.AccessRequests.SingleAsync(r => r.Id == pending.Id)).Status);
        Assert.False(await check.Context.SecretGrants.AnyAsync(share => share.SecretId == secret.Id));
        Assert.False(await check.Context.EmailLogs.AnyAsync(email => email.Body.Contains(secret.Name)));
        Assert.Equal([AuditAction.SecretCreated, AuditAction.AccessRequested], await check.Context.AuditLogs
            .Where(row => row.ResourceId == secret.Id).OrderBy(row => row.Sequence).Select(row => row.Action).ToArrayAsync());
    }

    private sealed class RefuseDecisionWrite : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("AccessRequests", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The store refused the decision write.");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

[Collection(SqlServerShareCollection.Name)]
public sealed class SqlServerAccessRequestSaveFailureTests(SqlServerShareDatabases databases) : AccessRequestSaveFailureTests(databases);

[Collection(PostgresShareCollection.Name)]
public sealed class PostgresAccessRequestSaveFailureTests(PostgresShareDatabases databases) : AccessRequestSaveFailureTests(databases);
