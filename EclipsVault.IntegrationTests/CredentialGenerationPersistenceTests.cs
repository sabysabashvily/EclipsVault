using EclipsVault.Core.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace EclipsVault.IntegrationTests;

public abstract class CredentialGenerationPersistenceTests(AccountChangeDatabases databases)
{
    [Fact]
    public async Task Upgrading_an_existing_account_starts_its_durable_generation_at_zero()
    {
        await using var upgrade = databases.NewRequest(database: "generation_upgrade");
        var migrator = upgrade.Context.GetService<IMigrator>();
        await migrator.MigrateAsync("AddAccountConcurrencyStamp");
        var accountId = Guid.NewGuid();
        await upgrade.Context.Database.ExecuteSqlAsync($"""
            INSERT INTO "Users" ("Id", "Username", "DisplayName", "Email", "NormalizedEmail", "PasswordHash",
                "PasswordSalt", "TotpEnabled", "Clearance", "ProjectKey", "IsDisabled", "FailedAccessCount", "CreatedAtUtc", "ConcurrencyStamp")
            VALUES ({accountId}, 'generation-upgrade', 'Generation Owner', 'upgrade@example.test', 'UPGRADE@EXAMPLE.TEST',
                {new byte[32]}, {new byte[16]}, {false}, 0, 'GLOBAL', {false}, 0, {DateTimeOffset.UtcNow}, {Guid.NewGuid()})
            """);

        await migrator.MigrateAsync();

        var stored = await upgrade.LoadAsync(accountId);
        Assert.Equal(0, stored.CredentialGeneration);
        Assert.Equal("Generation Owner", stored.DisplayName);
        Assert.Equal("upgrade@example.test", stored.Email);
    }

    [Fact]
    public async Task Recovery_replacement_and_authenticator_reset_commit_successive_generations()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-generation-persistence");
        await using (var replacement = databases.NewRequest())
        {
            Assert.Equal(1, (await replacement.Recovery.GenerateAsync(account.Id, 0, default)).CredentialGeneration);
        }
        await using (var reset = databases.NewRequest())
        {
            await reset.Profile.ResetOwnMfaAsync(account.Id, 1, default);
        }

        await using var check = databases.NewRequest();
        var stored = await check.LoadAsync(account.Id);
        Assert.Equal(2, stored.CredentialGeneration);
        Assert.False(stored.TotpEnabled);
        Assert.Empty(await check.Context.MfaRecoveryCodes.Where(code => code.UserId == account.Id).ToArrayAsync());
    }

    [Fact]
    public async Task A_proof_from_an_older_generation_cannot_replace_the_current_recovery_set()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-generation-stale-proof");
        await using (var first = databases.NewRequest())
        {
            await first.Recovery.GenerateAsync(account.Id, 0, default);
        }
        await using var stale = databases.NewRequest();

        await Assert.ThrowsAsync<StaleProofException>(() => stale.Recovery.GenerateAsync(account.Id, 0, default));

        await stale.Context.SaveChangesAsync();
        await using var check = databases.NewRequest();
        Assert.Equal(1, (await check.LoadAsync(account.Id)).CredentialGeneration);
        Assert.Equal(10, await check.RecoveryCodes.CountUnusedAsync(account.Id, default));
    }

    [Fact]
    public async Task A_rejected_audit_rolls_back_the_generation_and_cannot_be_rescued_by_a_later_save()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-generation-rollback");
        await using var rejected = databases.NewRequest();
        await databases.RejectAuditWritesAsync();
        try
        {
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => rejected.Recovery.GenerateAsync(account.Id, 0, default));
        }
        finally
        {
            await databases.RestoreAuditWritesAsync();
        }

        await rejected.Context.SaveChangesAsync();

        await using var check = databases.NewRequest();
        Assert.Equal(0, (await check.LoadAsync(account.Id)).CredentialGeneration);
        Assert.Equal(0, await check.RecoveryCodes.CountUnusedAsync(account.Id, default));
    }
}

[Collection(SqlServerAccountChangeCollection.Name)]
public sealed class SqlServerCredentialGenerationPersistenceTests(SqlServerAccountChangeDatabases databases)
    : CredentialGenerationPersistenceTests(databases);

[Collection(PostgresAccountChangeCollection.Name)]
public sealed class PostgresCredentialGenerationPersistenceTests(PostgresAccountChangeDatabases databases)
    : CredentialGenerationPersistenceTests(databases);
