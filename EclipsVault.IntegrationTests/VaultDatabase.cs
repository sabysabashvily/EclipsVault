using System.Security.Cryptography;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The database server under one vault host. Each run chooses the engine through
/// <see cref="ProviderVariable"/>, and every vault host in that run uses it. The vault creates its own
/// database there with its startup migrations, as a single-node install does.
/// </summary>
internal abstract class VaultDatabase : IAsyncDisposable
{
    public const string ProviderVariable = "ECLIPSVAULT_TEST_DATABASE_PROVIDER";

    /// <summary>
    /// SQL Server unless the run asks for PostgreSQL, so a plain local run is unchanged. A name that is
    /// neither fails the run, rather than testing SQL Server under a label that says PostgreSQL.
    /// </summary>
    public static string ProviderForThisRun => Environment.GetEnvironmentVariable(ProviderVariable) switch
    {
        null or "" => DatabaseProvider.SqlServer,
        var requested when requested.Equals(DatabaseProvider.SqlServer, StringComparison.OrdinalIgnoreCase) => DatabaseProvider.SqlServer,
        var requested when requested.Equals(DatabaseProvider.Postgres, StringComparison.OrdinalIgnoreCase) => DatabaseProvider.Postgres,
        var requested => throw new InvalidOperationException(
            $"{ProviderVariable} is '{requested}'. Set it to '{DatabaseProvider.SqlServer}' or '{DatabaseProvider.Postgres}', or leave it unset for SQL Server."),
    };

    /// <summary>The engine as <c>Database:Provider</c> names it.</summary>
    public abstract string Provider { get; }

    public static VaultDatabase ForThisRun() => ProviderForThisRun == DatabaseProvider.Postgres
        ? new PostgresVaultDatabase()
        : new SqlServerVaultDatabase();

    public abstract Task StartAsync();

    public abstract string ConnectionString();

    public abstract ValueTask DisposeAsync();

    protected static string ContainerName(string engine)
        => $"{Environment.GetEnvironmentVariable("ECLIPSVAULT_TEST_RESOURCE_PREFIX") ?? "eclipsvault-it"}-{engine}-{Guid.NewGuid():N}";
}

internal sealed class SqlServerVaultDatabase : VaultDatabase
{
    // The vault signs in to its database as sa, so the password is per run rather than Testcontainers'
    // published default. SQL Server requires upper and lower case, a digit and a symbol.
    private readonly MsSqlContainer _sql = new MsSqlBuilder(VaultFixture.SqlServerImage)
        .WithName(ContainerName("sql"))
        .WithPassword("Sa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    public override string Provider => DatabaseProvider.SqlServer;

    public override Task StartAsync() => _sql.StartAsync();

    public override string ConnectionString()
        => new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "EclipsVault" }.ConnectionString;

    public override ValueTask DisposeAsync() => _sql.DisposeAsync();
}

internal sealed class PostgresVaultDatabase : VaultDatabase
{
    // The vault signs in as the server's superuser, as it signs in to SQL Server as sa, so this
    // password is per run too.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresShareIssuerBackfill.PostgresImage)
        .WithName(ContainerName("postgres"))
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    public override string Provider => DatabaseProvider.Postgres;

    public override Task StartAsync() => _postgres.StartAsync();

    public override string ConnectionString()
        => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = "eclipsvault" }.ConnectionString;

    public override ValueTask DisposeAsync() => _postgres.DisposeAsync();
}
