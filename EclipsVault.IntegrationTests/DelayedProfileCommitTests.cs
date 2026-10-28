using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.IntegrationTests;

public abstract class DelayedProfileCommitTests(AccountChangeDatabases databases)
{
    [Fact]
    public Task Revoking_sessions_before_a_delayed_grant_is_consumed_prevents_the_change()
        => AssertDelayedRefusalAsync("it-grant-revoked", ProfileRefusal.OperationChanged,
            async (revocation, clock, accountId) => await revocation.RevokeAsync(accountId));

    [Fact]
    public Task A_grant_that_expires_before_delayed_consumption_cannot_change_the_account()
        => AssertDelayedRefusalAsync("it-grant-deadline", ProfileRefusal.VerificationExpired,
            (revocation, clock, accountId) => { clock.Advance(TimeSpan.FromMinutes(5)); return Task.CompletedTask; });

    private async Task AssertDelayedRefusalAsync(string username, ProfileRefusal expected,
        Func<InMemorySessionRevocationService, AdvancingClock, Guid, Task> changeWhileWaiting)
    {
        var account = await databases.CreateEnrolledAccountAsync(username);
        var clock = new AdvancingClock();
        var revocation = new InMemorySessionRevocationService(NullLogger<InMemorySessionRevocationService>.Instance);
        var version = await revocation.CaptureAsync(account.Id);
        var operation = new ProfileOperation
        {
            Id = Guid.NewGuid(),
            UserId = account.Id,
            SessionId = Guid.NewGuid(),
            Kind = ProfileOperationKind.ResetAuthenticator,
            RevocationVersion = version,
            CreatedAtUtc = clock.GetUtcNow(),
            ExpiresAtUtc = clock.GetUtcNow().AddMinutes(10),
            VerifiedAtUtc = clock.GetUtcNow(),
            ProofMethod = AccountProofMethod.PasswordAndAuthenticatorCode,
        };
        await using (var preparation = databases.NewRequest())
        {
            await preparation.ProfileOperations.ReplaceForSessionAsync(operation, clock.GetUtcNow(), default);
        }

        await using var request = databases.NewRequest();
        var delayed = new PausedConsumption(request.Changes);
        var changes = new VerifiedProfileChanges(request.Users, request.ProfileOperations, request.RecoveryCodes,
            request.Recovery, null!, null!, delayed, null!, revocation, clock);
        var committing = changes.ResetAuthenticatorAsync(new ProfileSession(account.Id, operation.SessionId, new SessionStamp(0, version)), operation.Id, default);
        await delayed.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            await changeWhileWaiting(revocation, clock, account.Id);
        }
        finally
        {
            delayed.Continue.SetResult();
        }

        var refused = await Assert.ThrowsAsync<ProfileOperationRefusedException>(() => committing);
        Assert.Equal(expected, refused.Refusal);
        await request.Context.SaveChangesAsync();
        await using var read = databases.NewRequest();
        var stored = await read.LoadAsync(account.Id);
        Assert.True(stored.TotpEnabled);
        Assert.Equal(account.TotpSecret, stored.TotpSecret);
        Assert.Equal(0, stored.CredentialGeneration);
        Assert.NotNull(await read.ProfileOperations.FindAsync(operation.Id, account.Id, operation.SessionId, default));
        Assert.False(await read.Context.AuditLogs.AnyAsync(row => row.ResourceId == account.Id));
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan time) => _now += time;
    }

    /// <summary>Pauses after the real account lock, before the final authorization check and real audited save.</summary>
    private sealed class PausedConsumption(IAccountChangeCommitter inner) : IAccountChangeCommitter
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> CommitAsync(User account, AuditAction action, string details, Func<CancellationToken, Task> recheck, CancellationToken ct)
            => inner.CommitAsync(account, action, details, async token =>
            {
                Reached.SetResult();
                await Continue.Task;
                await recheck(token);
            }, ct);

        public Task<bool> CommitAsync(User account, AuditAction action, string details, CancellationToken ct)
            => inner.CommitAsync(account, action, details, ct);

        public Task<bool> CommitAsync(User account, AuditEntry entry, CancellationToken ct)
            => inner.CommitAsync(account, entry, ct);
    }
}

[Collection(SqlServerAccountChangeCollection.Name)]
public sealed class SqlServerDelayedProfileCommitTests(SqlServerAccountChangeDatabases databases) : DelayedProfileCommitTests(databases);

[Collection(PostgresAccountChangeCollection.Name)]
public sealed class PostgresDelayedProfileCommitTests(PostgresAccountChangeDatabases databases) : DelayedProfileCommitTests(databases);
