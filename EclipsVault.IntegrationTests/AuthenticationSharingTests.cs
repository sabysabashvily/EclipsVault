using System.Data.Common;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(SqlServerAccountChangeCollection.Name)]
public sealed class AuthenticationSharingTests(SqlServerAccountChangeDatabases databases)
{
    [Fact]
    public async Task A_failed_sign_in_overlapping_a_share_to_its_account_commits_without_deadlocking()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-auth-share-overlap");
        var secret = new Secret
        {
            Id = Guid.NewGuid(),
            Name = "Concurrent sharing",
            ProjectKey = "GLOBAL",
            KekId = "test-kek",
            CreatedByUserId = account.Id,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await using (var setup = databases.NewRequest())
        {
            setup.Context.Secrets.Add(secret);
            await setup.Context.SaveChangesAsync();
        }

        var overlap = new ShareAuthenticationOverlap();
        await using var sharing = databases.NewRequest(overlap.Sharing);
        await using var authentication = databases.NewRequest(overlap.Authentication);
        var loaded = await authentication.LoadAsync(account.Id);
        sharing.Context.SecretGrants.Add(new SecretGrant
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            GranteeUserId = account.Id,
            GranteeUsername = account.Username,
            GrantedBy = "test-issuer",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var share = sharing.Context.SaveChangesAsync(deadline.Token);
        try
        {
            await overlap.ShareReady.WaitAsync(deadline.Token);

            var signIn = authentication.Attempt.RunAsync(loaded, async token =>
            {
                await authentication.Users.RecordFailedAttemptAsync(loaded, LockoutPolicy.Default, DateTimeOffset.UtcNow, token);
                authentication.Attempt.RecordUserEvent(AuditAction.LoginFailed, loaded, "Incorrect password");
                return false;
            }, deadline.Token);
            await Task.WhenAll(share, signIn);
        }
        finally
        {
            overlap.ReleaseShare();
            await share;
        }

        await using var check = databases.NewRequest();
        Assert.Equal(1, (await check.LoadAsync(account.Id)).FailedAccessCount);
        Assert.Equal(account.Id, (await check.Context.SecretGrants.SingleAsync(grant => grant.SecretId == secret.Id)).GranteeUserId);
        Assert.Equal([AuditAction.SecretShared, AuditAction.LoginFailed],
            await check.Context.AuditLogs.Where(row => (row.ResourceId == secret.Id && row.Action == AuditAction.SecretShared) || row.ResourceId == account.Id)
                .OrderBy(row => row.Sequence).Select(row => row.Action).ToArrayAsync());
    }

    /// <summary>
    /// Pauses the share after it holds the chain, before its foreign-key check. Releasing it when
    /// authentication asks for the chain recreates the lock cycle if authentication wrote User first.
    /// </summary>
    private sealed class ShareAuthenticationOverlap
    {
        private readonly TaskCompletionSource _shareReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseShare = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ShareAuthenticationOverlap()
        {
            Sharing = new PauseShare(this);
            Authentication = new ReleaseShareAtChain(this);
        }

        public IInterceptor Sharing { get; }

        public IInterceptor Authentication { get; }

        public Task ShareReady => _shareReady.Task;

        public void ReleaseShare() => _releaseShare.TrySetResult();

        private sealed class PauseShare(ShareAuthenticationOverlap overlap) : DbCommandInterceptor
        {
            public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                if (command.CommandText.Contains("INSERT INTO [SecretGrants]", StringComparison.Ordinal))
                {
                    overlap._shareReady.TrySetResult();
                    await overlap._releaseShare.Task.WaitAsync(cancellationToken);
                }

                return result;
            }
        }

        private sealed class ReleaseShareAtChain(ShareAuthenticationOverlap overlap) : DbCommandInterceptor
        {
            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, "EclipsVault:AuditChain")))
                {
                    overlap.ReleaseShare();
                }

                return ValueTask.FromResult(result);
            }
        }
    }
}
