using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence.Repositories;
using EclipsVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.IntegrationTests;

public abstract class ProfileOperationPersistenceTests(AccountChangeDatabases databases)
{
    [Fact]
    public async Task A_sessions_replacement_preserves_another_sessions_operation()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-operations-replacement");
        var first = Operation(account.Id);
        var other = Operation(account.Id);
        var replacement = Operation(account.Id);
        replacement.SessionId = first.SessionId;
        await StoreAsync(first);
        await StoreAsync(other);

        await StoreAsync(replacement);

        await using var read = databases.NewRequest();
        Assert.Equal(2, await read.Context.ProfileOperations.CountAsync(o => o.UserId == account.Id));
        Assert.Null(await read.ProfileOperations.FindAsync(first.Id, account.Id, first.SessionId, default));
        Assert.NotNull(await read.ProfileOperations.FindAsync(other.Id, account.Id, other.SessionId, default));
        Assert.Null(await read.ProfileOperations.FindAsync(other.Id, account.Id, first.SessionId, default));
    }

    [Fact]
    public async Task Simultaneous_preparations_cannot_exceed_the_five_operation_account_limit()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-operations-admission");
        await StoreAsync(Operation(account.Id));
        await StoreAsync(Operation(account.Id));
        await StoreAsync(Operation(account.Id));
        await StoreAsync(Operation(account.Id));

        var admitted = await Task.WhenAll(TryStoreAsync(Operation(account.Id)), TryStoreAsync(Operation(account.Id)));

        Assert.Equal([false, true], admitted.Order());
        await using var read = databases.NewRequest();
        Assert.Equal(5, await read.Context.ProfileOperations.CountAsync(o => o.UserId == account.Id));
    }

    [Fact]
    public async Task Only_one_request_can_consume_a_passkey_challenge()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-operations-challenge");
        var operation = Operation(account.Id);
        operation.Challenge = "one-challenge";
        operation.ChallengePurpose = PasskeyCeremonyPurpose.AccountVerification;
        operation.ChallengeExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(120);
        await StoreAsync(operation);

        var accepted = await Task.WhenAll(ConsumeChallengeAsync(operation), ConsumeChallengeAsync(operation));

        Assert.Equal([false, true], accepted.Order());
    }

    [Fact]
    public async Task A_different_purpose_does_not_consume_the_current_unexpired_prompt()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-operations-purpose");
        var operation = Operation(account.Id);
        operation.Challenge = "registration-only";
        operation.ChallengePurpose = PasskeyCeremonyPurpose.Registration;
        operation.ChallengeExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(120);
        await StoreAsync(operation);
        await using var request = databases.NewRequest();
        var stored = (await request.ProfileOperations.FindAsync(operation.Id, account.Id, operation.SessionId, default))!;

        Assert.False(await request.ProfileOperations.TryConsumeChallengeAsync(stored, PasskeyCeremonyPurpose.AccountVerification, DateTimeOffset.UtcNow, default));
        Assert.Equal("registration-only", (await request.Context.ProfileOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id)).Challenge);
    }

    [Fact]
    public async Task An_expired_prompt_cannot_be_consumed_for_its_matching_purpose()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-operations-expired-prompt");
        var operation = Operation(account.Id);
        operation.Challenge = "expired-registration";
        operation.ChallengePurpose = PasskeyCeremonyPurpose.Registration;
        operation.ChallengeExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        await StoreAsync(operation);
        await using var request = databases.NewRequest();
        var stored = (await request.ProfileOperations.FindAsync(operation.Id, account.Id, operation.SessionId, default))!;

        Assert.False(await request.ProfileOperations.TryConsumeChallengeAsync(stored, PasskeyCeremonyPurpose.Registration, DateTimeOffset.UtcNow, default));

        Assert.Equal("expired-registration", (await request.Context.ProfileOperations.AsNoTracking().SingleAsync(o => o.Id == operation.Id)).Challenge);
    }

    [Fact]
    public async Task Competing_final_consumers_commit_one_generation_and_one_event()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-operations-final");
        var operation = Operation(account.Id);
        operation.VerifiedAtUtc = DateTimeOffset.UtcNow;
        operation.ProofMethod = AccountProofMethod.PasswordAndAuthenticatorCode;
        await StoreAsync(operation);
        await using var first = databases.NewRequest();
        await using var second = databases.NewRequest();
        var accountA = await first.LoadAsync(account.Id);
        var accountB = await second.LoadAsync(account.Id);
        first.ProfileOperations.StageConsumption((await first.ProfileOperations.FindAsync(operation.Id, account.Id, operation.SessionId, default))!);
        second.ProfileOperations.StageConsumption((await second.ProfileOperations.FindAsync(operation.Id, account.Id, operation.SessionId, default))!);
        accountA.CredentialGeneration++;
        accountB.CredentialGeneration++;

        await first.Changes.CommitAsync(accountA, AuditAction.PasswordChanged, "Password changed", default);
        await Assert.ThrowsAsync<AccountChangedException>(() => second.Changes.CommitAsync(accountB, AuditAction.PasswordChanged, "Password changed", default));

        await using var read = databases.NewRequest();
        Assert.Equal(1, (await read.LoadAsync(account.Id)).CredentialGeneration);
        Assert.Empty(await read.Context.ProfileOperations.Where(o => o.UserId == account.Id).ToArrayAsync());
        Assert.Equal(1, await read.Context.AuditLogs.CountAsync(row => row.ResourceId == account.Id && row.Action == AuditAction.PasswordChanged));
    }

    [Fact]
    public async Task Rejected_auditing_restores_the_grant_and_leaves_no_staged_change_to_rescue()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-operations-rollback");
        var operation = Operation(account.Id);
        operation.VerifiedAtUtc = DateTimeOffset.UtcNow;
        await StoreAsync(operation);
        await databases.RejectAuditWritesAsync();
        try
        {
            await using var request = databases.NewRequest();
            var loaded = await request.LoadAsync(account.Id);
            request.ProfileOperations.StageConsumption((await request.ProfileOperations.FindAsync(operation.Id, account.Id, operation.SessionId, default))!);
            loaded.CredentialGeneration++;
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => request.Changes.CommitAsync(loaded, AuditAction.PasswordChanged, "Password changed", default));
            await request.Context.SaveChangesAsync();

            await using var read = databases.NewRequest();
            Assert.Equal(0, (await read.LoadAsync(account.Id)).CredentialGeneration);
            Assert.NotNull(await read.ProfileOperations.FindAsync(operation.Id, account.Id, operation.SessionId, default));
        }
        finally
        {
            await databases.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Last_key_removal_waits_for_redemption_then_refuses_the_lost_fallback()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-operations-last-key");
        await using var setup = databases.NewRequest();
        var codes = (await setup.Recovery.GenerateAsync(account.Id, 0, default)).Codes;
        var key = new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            UserId = account.Id,
            CredentialId = [1, 2, 3],
            PublicKey = [1],
            Nickname = "Last key",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        setup.Context.PasskeyCredentials.Add(key);
        await setup.Context.SaveChangesAsync();
        await SpendFirstNineAsync(account.Id, codes);
        var operation = Operation(account.Id);
        operation.Kind = ProfileOperationKind.RemovePasskey;
        operation.TargetPasskeyId = key.Id;
        var revocation = new InMemorySessionRevocationService(NullLogger<InMemorySessionRevocationService>.Instance);
        operation.RevocationVersion = await revocation.CaptureAsync(account.Id);
        operation.CredentialGeneration = 1;
        operation.VerifiedAtUtc = DateTimeOffset.UtcNow;
        operation.ProofMethod = AccountProofMethod.PasswordAndRecoveryCode;
        await StoreAsync(operation);
        await using var redemption = databases.NewRequest();
        var redeemer = await redemption.LoadAsync(account.Id);
        var codeSpent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var redeeming = redemption.Attempt.RunAsync(redeemer, async token =>
        {
            Assert.True(await redemption.SecondFactors.TryRedeemRecoveryCodeAsync(redeemer, codes[9], token));
            codeSpent.SetResult();
            await allowCommit.Task;
            return true;
        }, default);
        await codeSpent.Task;
        await using var deletion = databases.NewRequest();
        var changes = new VerifiedProfileChanges(deletion.Users, deletion.ProfileOperations, deletion.RecoveryCodes,
            deletion.Recovery, new PasskeyCredentialRepository(deletion.Context), null!, deletion.Changes, null!, revocation, TimeProvider.System);
        var deleting = changes.RemovePasskeyAsync(new ProfileSession(account.Id, operation.SessionId,
            new SessionStamp(1, operation.RevocationVersion)), operation.Id, key.Id, default);
        allowCommit.SetResult();
        await redeeming;

        var refusal = await Assert.ThrowsAsync<ProfileOperationRefusedException>(() => deleting);

        Assert.Equal(ProfileRefusal.AlternateMethodRequired, refusal.Refusal);
        await using var read = databases.NewRequest();
        Assert.True(await read.Context.PasskeyCredentials.AnyAsync(p => p.Id == key.Id));
        Assert.Equal(1, (await read.LoadAsync(account.Id)).CredentialGeneration);
        Assert.NotNull(await read.ProfileOperations.FindAsync(operation.Id, account.Id, operation.SessionId, default));
    }

    private async Task SpendFirstNineAsync(Guid accountId, IReadOnlyList<string> codes)
    {
        await using var request = databases.NewRequest();
        var account = await request.LoadAsync(accountId);
        Assert.True(await request.SecondFactors.TryRedeemRecoveryCodeAsync(account, codes[0], default));
        Assert.True(await request.SecondFactors.TryRedeemRecoveryCodeAsync(account, codes[1], default));
        Assert.True(await request.SecondFactors.TryRedeemRecoveryCodeAsync(account, codes[2], default));
        Assert.True(await request.SecondFactors.TryRedeemRecoveryCodeAsync(account, codes[3], default));
        Assert.True(await request.SecondFactors.TryRedeemRecoveryCodeAsync(account, codes[4], default));
        Assert.True(await request.SecondFactors.TryRedeemRecoveryCodeAsync(account, codes[5], default));
        Assert.True(await request.SecondFactors.TryRedeemRecoveryCodeAsync(account, codes[6], default));
        Assert.True(await request.SecondFactors.TryRedeemRecoveryCodeAsync(account, codes[7], default));
        Assert.True(await request.SecondFactors.TryRedeemRecoveryCodeAsync(account, codes[8], default));
    }

    private async Task StoreAsync(ProfileOperation operation)
    {
        await using var request = databases.NewRequest();
        await request.ProfileOperations.ReplaceForSessionAsync(operation, DateTimeOffset.UtcNow, default);
    }

    private async Task<bool> TryStoreAsync(ProfileOperation operation)
    {
        try
        {
            await StoreAsync(operation);
            return true;
        }
        catch (ProfileOperationRefusedException failure) when (failure.Refusal == ProfileRefusal.Throttled)
        {
            return false;
        }
    }

    private async Task<bool> ConsumeChallengeAsync(ProfileOperation operation)
    {
        await using var request = databases.NewRequest();
        return await request.ProfileOperations.TryConsumeChallengeAsync(operation, PasskeyCeremonyPurpose.AccountVerification, DateTimeOffset.UtcNow, default);
    }

    private static ProfileOperation Operation(Guid accountId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = accountId,
        SessionId = Guid.NewGuid(),
        Kind = ProfileOperationKind.ResetAuthenticator,
        RevocationVersion = "incarnation:version",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ProfileOperationLimits.VerificationWindow),
    };
}

[Collection(SqlServerAccountChangeCollection.Name)]
public sealed class SqlServerProfileOperationPersistenceTests(SqlServerAccountChangeDatabases databases) : ProfileOperationPersistenceTests(databases);

[Collection(PostgresAccountChangeCollection.Name)]
public sealed class PostgresProfileOperationPersistenceTests(PostgresAccountChangeDatabases databases) : ProfileOperationPersistenceTests(databases);
