using System.Security.Cryptography;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// One database server holding a fresh database for each case of email identity: one upgraded from
/// accounts made before emails were normalized, one created at the current schema, and one per
/// upgrade that must be refused.
/// </summary>
public abstract class EmailIdentityDatabases : IAsyncLifetime
{
    private const string MigrationBeforeEmailIdentity = "AddShareIssuerUserId";
    private const string UpgradedDatabase = "upgraded";
    private const string CurrentDatabase = "current";

    public static readonly Guid MixedCaseAccount = new("30000000-0000-0000-0000-000000000001");
    public static readonly Guid PaddedAccount = new("30000000-0000-0000-0000-000000000002");
    public static readonly Guid DisabledAccount = new("30000000-0000-0000-0000-000000000003");
    public static readonly Guid AccountNamedByItsOwnEmail = new("30000000-0000-0000-0000-000000000004");
    public static readonly Guid DottedAccount = new("30000000-0000-0000-0000-000000000005");
    public static readonly Guid UndottedAccount = new("30000000-0000-0000-0000-000000000006");

    public static readonly Guid UnicodeAccount = new("30000000-0000-0000-0000-000000000007");
    public static readonly Guid SupplementaryAccount = new("30000000-0000-0000-0000-000000000008");

    public const string MixedCaseEmail = "Mixed.Case@Example.TEST";
    public const string PaddedEmail = "\u00A0\tpadded@example.test\u3000 ";

    private readonly List<EclipsVaultDbContext> _contexts = [];

    protected abstract string Provider { get; }

    protected abstract Task StartServerAsync();

    protected abstract Task StopServerAsync();

    protected abstract string ConnectionStringFor(string database);

    public async Task InitializeAsync()
    {
        await StartServerAsync();

        var upgraded = await CreateBeforeEmailIdentityAsync(UpgradedDatabase);
        await InsertLegacyAccountAsync(upgraded, MixedCaseAccount, "mixed-case", MixedCaseEmail);
        await InsertLegacyAccountAsync(upgraded, PaddedAccount, "padded", PaddedEmail);
        await InsertLegacyAccountAsync(upgraded, DisabledAccount, "disabled", "Disabled@Example.Test", disabled: true);
        await InsertLegacyAccountAsync(upgraded, AccountNamedByItsOwnEmail, "self@example.test", "Self@Example.Test");
        await InsertLegacyAccountAsync(upgraded, DottedAccount, "dotted", "dots.and+tags@example.test");
        await InsertLegacyAccountAsync(upgraded, UndottedAccount, "undotted", "dotsand@example.test");
        await InsertLegacyAccountAsync(upgraded, UnicodeAccount, "unicode", "Øyvind@Example.Test");
        await InsertLegacyAccountAsync(upgraded, SupplementaryAccount, "supplementary", "\U00010C28@Example.Test");
        await UpgradeAsync(upgraded);

        await Context(CurrentDatabase).GetService<IMigrator>().MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.DisposeAsync();
        }

        await StopServerAsync();
    }

    /// <summary>Accounts in the database upgraded from accounts made before emails were normalized.</summary>
    public UserRepository UpgradedUsers() => new(Context(UpgradedDatabase), TimeProvider.System);

    /// <summary>Accounts in a database created at the current schema, shared by every test using it.</summary>
    public UserRepository CurrentUsers() => new(Context(CurrentDatabase), TimeProvider.System);

    public Task<string> UpgradedEmailOfAsync(Guid accountId)
        => Context(UpgradedDatabase).Database
            .SqlQuery<string>($"""SELECT "Email" AS "Value" FROM "Users" WHERE "Id" = {accountId}""")
            .SingleAsync();

    /// <summary>A database at the schema before emails were normalized, for an upgrade a test runs itself.</summary>
    public async Task<EclipsVaultDbContext> CreateBeforeEmailIdentityAsync(string database)
    {
        var context = Context(database);
        await context.GetService<IMigrator>().MigrateAsync(MigrationBeforeEmailIdentity);
        return context;
    }

    public static Task UpgradeAsync(EclipsVaultDbContext context) => context.GetService<IMigrator>().MigrateAsync();

    // The schema is the one before emails were normalized, which the current model no longer
    // describes, so rows go in as SQL. Double-quoted names work on both providers.
    public static Task InsertLegacyAccountAsync(
        EclipsVaultDbContext context, Guid id, string username, string email, bool disabled = false)
        => context.Database.ExecuteSqlAsync($"""
            INSERT INTO "Users" ("Id", "Username", "DisplayName", "Email", "PasswordHash", "PasswordSalt",
                "TotpEnabled", "Clearance", "ProjectKey", "IsDisabled", "FailedAccessCount", "CreatedAtUtc")
            VALUES ({id}, {username}, {username}, {email}, {new byte[32]}, {new byte[16]},
                {false}, {(int)ClearanceLevel.Standard}, 'GLOBAL', {disabled}, 0, {DateTimeOffset.UtcNow})
            """);

    public static Task<string[]> EmailsAsync(EclipsVaultDbContext context)
        => context.Database
            .SqlQuery<string>($"""
                SELECT "Email" AS "Value" FROM "Users" ORDER BY "Id"
                """)
            .ToArrayAsync();

    public static async Task<string?> LatestMigrationAsync(EclipsVaultDbContext context)
        => (await context.Database.GetAppliedMigrationsAsync()).LastOrDefault();

    public static User NewAccount(string username, string email) => new()
    {
        Id = Guid.NewGuid(),
        Username = username,
        DisplayName = username,
        Email = email,
        PasswordHash = new byte[32],
        PasswordSalt = new byte[16],
        ProjectKey = "GLOBAL",
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    private EclipsVaultDbContext Context(string database)
    {
        var options = new DbContextOptionsBuilder<EclipsVaultDbContext>();
        options.UseVaultDatabase(Provider, ConnectionStringFor(database));
        var context = new EclipsVaultDbContext(options.Options);
        _contexts.Add(context);
        return context;
    }
}

public sealed class SqlServerEmailIdentityDatabases : EmailIdentityDatabases
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder(VaultFixture.SqlServerImage)
        .WithName(ShareIssuerBackfill.ContainerName("sql"))
        .WithPassword("Sa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.SqlServer;

    protected override Task StartServerAsync() => _sql.StartAsync();

    protected override Task StopServerAsync() => _sql.DisposeAsync().AsTask();

    protected override string ConnectionStringFor(string database)
        => new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = database }.ConnectionString;
}

public sealed class PostgresEmailIdentityDatabases : EmailIdentityDatabases
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresShareIssuerBackfill.PostgresImage)
        .WithName(ShareIssuerBackfill.ContainerName("postgres"))
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.Postgres;

    protected override Task StartServerAsync() => _postgres.StartAsync();

    protected override Task StopServerAsync() => _postgres.DisposeAsync().AsTask();

    protected override string ConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class SqlServerEmailIdentityCollection : ICollectionFixture<SqlServerEmailIdentityDatabases>
{
    public const string Name = "email identity on SQL Server";
}

[CollectionDefinition(Name)]
public sealed class PostgresEmailIdentityCollection : ICollectionFixture<PostgresEmailIdentityDatabases>
{
    public const string Name = "email identity on PostgreSQL";
}
