using System.Data.Common;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace EclipsVault.IntegrationTests;

public abstract class AccountTransactionFailureTests(AccountChangeDatabases databases)
{
    [Fact]
    public async Task A_refused_authentication_event_keeps_the_code_unspent_after_a_later_save()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-provider-ledger-totp");
        await using var request = databases.NewRequest();
        var loaded = await request.LoadAsync(account.Id);
        var code = Totp.Code(account.TotpSecret!, Totp.CurrentStep());

        await databases.RejectAuditWritesAsync();
        try
        {
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => request.Attempt.RunAsync(loaded, async token =>
            {
                var accepted = await request.SecondFactors.TryAcceptAuthenticatorCodeAsync(loaded, code, token);
                request.Attempt.RecordUserEvent(AuditAction.LoginSucceeded, loaded, "Password + TOTP");
                return accepted;
            }, default));
        }
        finally
        {
            await databases.RestoreAuditWritesAsync();
        }

        await request.Context.SaveChangesAsync();
        await using var check = databases.NewRequest();
        Assert.Null((await check.LoadAsync(account.Id)).LastTotpStep);
        Assert.Empty(await check.Context.AuditLogs.Where(row => row.ResourceId == account.Id).ToArrayAsync());
    }

    [Fact]
    public async Task A_refused_recovery_event_keeps_every_code_unused_after_a_later_save()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-provider-ledger-recovery");
        await using var request = databases.NewRequest();
        var codes = (await request.Recovery.GenerateAsync(account.Id, 0, default)).Codes;
        var loaded = await request.LoadAsync(account.Id);

        await databases.RejectAuditWritesAsync();
        try
        {
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => request.Attempt.RunAsync(loaded, async token =>
            {
                var redeemed = await request.SecondFactors.TryRedeemRecoveryCodeAsync(loaded, codes[0], token);
                request.Attempt.RecordUserEvent(AuditAction.RecoveryCodeUsed, loaded, "Recovery code redeemed");
                return redeemed;
            }, default));
        }
        finally
        {
            await databases.RestoreAuditWritesAsync();
        }

        await request.Context.SaveChangesAsync();
        await using var check = databases.NewRequest();
        Assert.Equal(10, await check.RecoveryCodes.CountUnusedAsync(account.Id, default));
        Assert.Equal(AuditAction.RecoveryCodesGenerated,
            (await check.Context.AuditLogs.SingleAsync(row => row.ResourceId == account.Id)).Action);
    }

    [Fact]
    public async Task A_canceled_recovery_read_cannot_leave_an_authenticator_reset_for_a_later_save()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-cleanup-staging");
        await using (var setup = databases.NewRequest())
        {
            await setup.Recovery.GenerateAsync(account.Id, 0, default);
        }

        await using var request = databases.NewRequest(new CancelRecoveryRead());
        var before = await request.Context.AuditLogs.CountAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => request.Profile.ResetOwnMfaAsync(account.Id, 1, default));
        await request.Context.SaveChangesAsync();

        await using var check = databases.NewRequest();
        var stored = await check.LoadAsync(account.Id);
        Assert.True(stored.TotpEnabled);
        Assert.Equal(account.TotpSecret, stored.TotpSecret);
        Assert.Equal(10, await check.RecoveryCodes.CountUnusedAsync(account.Id, default));
        Assert.Equal(before, await check.Context.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task A_transaction_that_cannot_start_leaves_no_account_edit_for_a_later_save()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-cleanup-begin");
        await using var request = databases.NewRequest(new RefuseTransactionStart());
        var loaded = await request.LoadAsync(account.Id);
        loaded.DisplayName = "Never committed";

        await Assert.ThrowsAsync<AuditWriteFailedException>(() =>
            request.Changes.CommitAsync(loaded, AuditAction.ProfileUpdated, "Display name updated", default));
        await request.Context.SaveChangesAsync();

        await using var check = databases.NewRequest();
        Assert.Equal(account.DisplayName, (await check.LoadAsync(account.Id)).DisplayName);
        Assert.Empty(await check.Context.AuditLogs.Where(row => row.ResourceId == account.Id).ToArrayAsync());
    }

    [Fact]
    public async Task A_rollback_error_cannot_leave_a_refused_account_edit_for_a_later_save()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-cleanup-rollback");
        var fault = new RefuseRollbackCompletion();
        await using var request = databases.NewRequest(fault);
        var loaded = await request.LoadAsync(account.Id);
        await Assert.ThrowsAsync<AuditWriteFailedException>(() => request.Attempt.RunAsync<bool>(loaded, async token =>
        {
            await request.Users.TryAcceptTotpStepAsync(loaded, Totp.CurrentStep(), token);
            loaded.DisplayName = "Never committed";
            throw new InvalidOperationException("The attempt failed before saving its staged account edit.");
        }, default));
        await request.Context.SaveChangesAsync();

        await using var check = databases.NewRequest();
        Assert.True(fault.WasReached);
        var stored = await check.LoadAsync(account.Id);
        Assert.Equal(account.DisplayName, stored.DisplayName);
        Assert.Null(stored.LastTotpStep);
        Assert.Empty(await check.Context.AuditLogs.Where(row => row.ResourceId == account.Id).ToArrayAsync());
    }

    private sealed class CancelRecoveryRead : DbCommandInterceptor
    {
        private bool _canceled;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!_canceled && command.CommandText.Contains("MfaRecoveryCodes", StringComparison.Ordinal))
            {
                _canceled = true;
                throw new OperationCanceledException("The recovery-code staging read was canceled.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class RefuseTransactionStart : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The account transaction could not start.");
    }

    private sealed class RefuseRollbackCompletion : DbTransactionInterceptor
    {
        public bool WasReached { get; private set; }

        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            WasReached = true;
            throw new InvalidOperationException("Rollback completed but reporting its completion failed.");
        }
    }
}

[Collection(SqlServerAccountChangeCollection.Name)]
public sealed class SqlServerAccountTransactionFailureTests(SqlServerAccountChangeDatabases databases)
    : AccountTransactionFailureTests(databases);

[Collection(PostgresAccountChangeCollection.Name)]
public sealed class PostgresAccountTransactionFailureTests(PostgresAccountChangeDatabases databases)
    : AccountTransactionFailureTests(databases);
