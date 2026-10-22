using System.Security.Cryptography;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A fresh database holding shares made before a share recorded its issuer's account, migrated once
/// through the migration that fills that account in. Each share is one case of that backfill.
/// </summary>
public abstract class ShareIssuerBackfill : IAsyncLifetime
{
    private const string MigrationBeforeIssuerAccounts = "AddFeatureEvaluations";

    private static readonly DateTimeOffset SharedAt = new(2026, 3, 2, 9, 30, 0, TimeSpan.Zero);

    public static readonly Guid AccountStillHere = new("10000000-0000-0000-0000-000000000001");
    public static readonly Guid AccountCreatedAsItShared = new("10000000-0000-0000-0000-000000000002");
    public static readonly Guid AccountWithDifferentlyCasedUsername = new("10000000-0000-0000-0000-000000000003");

    public static readonly Guid ShareByAnAccountStillHere = new("20000000-0000-0000-0000-000000000001");
    public static readonly Guid ShareByAnAccountCreatedAtTheSameInstant = new("20000000-0000-0000-0000-000000000002");
    public static readonly Guid ShareByADeletedAccountWhoseUsernameWasReused = new("20000000-0000-0000-0000-000000000003");
    public static readonly Guid ShareByAnUnknownUsername = new("20000000-0000-0000-0000-000000000004");
    public static readonly Guid ShareByADifferentlyCasedUsername = new("20000000-0000-0000-0000-000000000005");

    private EclipsVaultDbContext? _db;

    protected abstract string Provider { get; }

    /// <summary>Starts the database server and returns a connection string to a database the migrations may create.</summary>
    protected abstract Task<string> StartDatabaseAsync();

    protected abstract Task StopDatabaseAsync();

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<EclipsVaultDbContext>();
        options.UseVaultDatabase(Provider, await StartDatabaseAsync());
        _db = new EclipsVaultDbContext(options.Options);
        var migrator = _db.GetService<IMigrator>();

        await migrator.MigrateAsync(MigrationBeforeIssuerAccounts);
        await InsertOldSharesAsync(_db);
        await migrator.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        await StopDatabaseAsync();
    }

    public Task<Guid?> IssuerOfAsync(Guid shareId)
        => _db!.SecretGrants.Where(g => g.Id == shareId).Select(g => g.IssuerUserId).SingleAsync();

    private static async Task InsertOldSharesAsync(EclipsVaultDbContext db)
    {
        var aMonthBefore = SharedAt.AddDays(-30);
        var aMonthAfter = SharedAt.AddDays(30);
        var recipient = Guid.NewGuid();
        await InsertAccountAsync(db, recipient, "recipient", aMonthBefore);

        await InsertAccountAsync(db, AccountStillHere, "issuer-still-here", aMonthBefore);
        await InsertShareAsync(db, ShareByAnAccountStillHere, recipient, "issuer-still-here");

        await InsertAccountAsync(db, AccountCreatedAsItShared, "issuer-same-instant", SharedAt);
        await InsertShareAsync(db, ShareByAnAccountCreatedAtTheSameInstant, recipient, "issuer-same-instant");

        // The account that made this share was deleted, and its username given to a later account.
        await InsertAccountAsync(db, Guid.NewGuid(), "issuer-reused", aMonthAfter);
        await InsertShareAsync(db, ShareByADeletedAccountWhoseUsernameWasReused, recipient, "issuer-reused");

        await InsertShareAsync(db, ShareByAnUnknownUsername, recipient, "issuer-unknown");

        await InsertAccountAsync(db, AccountWithDifferentlyCasedUsername, "issuer-cased", aMonthBefore);
        await InsertShareAsync(db, ShareByADifferentlyCasedUsername, recipient, "Issuer-Cased");
    }

    // The schema is the one before the issuer's account existed, which the current model no longer
    // describes, so rows go in as SQL. Double-quoted names work on both providers.
    private static Task InsertAccountAsync(EclipsVaultDbContext db, Guid id, string username, DateTimeOffset createdAt)
        => db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Users" ("Id", "Username", "DisplayName", "Email", "PasswordHash", "PasswordSalt",
                "TotpEnabled", "Clearance", "ProjectKey", "IsDisabled", "FailedAccessCount", "CreatedAtUtc")
            VALUES ({id}, {username}, {username}, {username + "@example.test"}, {new byte[32]}, {new byte[16]},
                {false}, {(int)ClearanceLevel.Standard}, 'GLOBAL', {false}, 0, {createdAt})
            """);

    /// <summary>A share on a secret of its own, since a recipient holds one share per secret.</summary>
    private static async Task InsertShareAsync(EclipsVaultDbContext db, Guid shareId, Guid recipient, string grantedBy)
    {
        var secretId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Secrets" ("Id", "Name", "ProjectKey", "Environment", "Sensitivity", "Ciphertext", "WrappedDek",
                "KekId", "Algorithm", "CreatedAtUtc", "CreatedByUserId", "IsHoneyToken", "IsShredded")
            VALUES ({secretId}, {"secret-" + shareId}, 'GLOBAL', {(int)SecretEnvironment.Development},
                {(int)SensitivityLevel.Internal}, {new byte[] { 1 }}, {new byte[] { 1 }},
                'test-kek', 'AES-256-GCM', {SharedAt}, {Guid.Empty}, {false}, {false})
            """);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "SecretGrants" ("Id", "SecretId", "GranteeUserId", "GranteeUsername", "GrantedBy", "CreatedAtUtc")
            VALUES ({shareId}, {secretId}, {recipient}, 'recipient', {grantedBy}, {SharedAt})
            """);
    }

    internal static string ContainerName(string engine)
        => $"{Environment.GetEnvironmentVariable("ECLIPSVAULT_TEST_RESOURCE_PREFIX") ?? "eclipsvault-it"}-migration-{engine}-{Guid.NewGuid():N}";
}

public sealed class SqlServerShareIssuerBackfill : ShareIssuerBackfill
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder(VaultFixture.SqlServerImage)
        .WithName(ContainerName("sql"))
        .WithPassword("Sa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.SqlServer;

    protected override async Task<string> StartDatabaseAsync()
    {
        await _sql.StartAsync();
        return new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "EclipsVault" }.ConnectionString;
    }

    protected override Task StopDatabaseAsync() => _sql.DisposeAsync().AsTask();
}

public sealed class PostgresShareIssuerBackfill : ShareIssuerBackfill
{
    /// <summary>PostgreSQL 17, the version the demo and deployment compose files run, pinned by digest.</summary>
    internal const string PostgresImage =
        "postgres:17@sha256:2d2b8998d31037bf721cfdf764d76ba74171b4fab3431b7f72c27c56ddbdf9e3";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresImage)
        .WithName(ContainerName("postgres"))
        .WithDatabase("eclipsvault")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.Postgres;

    protected override async Task<string> StartDatabaseAsync()
    {
        await _postgres.StartAsync();
        return _postgres.GetConnectionString();
    }

    protected override Task StopDatabaseAsync() => _postgres.DisposeAsync().AsTask();
}
