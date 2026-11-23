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

    /// <summary>
    /// A SQL Server database for the vault to mint dynamic credentials on, as an operator configures one:
    /// never the vault's own database. Returns the connection string the vault is given for it.
    /// </summary>
    public abstract Task<string> StartDynamicTargetAsync();

    private PostgreSqlContainer? _postgresTarget;

    /// <summary>
    /// A PostgreSQL server for <c>Postgres</c> roles, always a server of its own: a PostgreSQL role spans
    /// every database on its server, so a target beside the vault's database would reach it. The vault
    /// signs in as the least-privilege login docs/INSTALL.md describes, never the superuser, so the
    /// tests prove those grants are enough. Returns the connection string the vault is given for it.
    /// </summary>
    public async Task<string> StartPostgresDynamicTargetAsync()
    {
        _postgresTarget = NewPostgresContainer("postgres-target");
        await _postgresTarget.StartAsync();
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await using (var server = new NpgsqlConnection(_postgresTarget.GetConnectionString()))
        {
            await server.OpenAsync();
            await using var setUp = server.CreateCommand();
            // The password is hex, generated above.
            setUp.CommandText = $"""
                CREATE ROLE {PostgresTargetLogin} LOGIN PASSWORD '{password}' CREATEROLE;
                GRANT pg_read_all_data, pg_write_all_data TO {PostgresTargetLogin} WITH ADMIN TRUE, INHERIT FALSE, SET FALSE;
                ALTER ROLE {PostgresTargetLogin} SET createrole_self_grant = 'inherit';
                """;
            await setUp.ExecuteNonQueryAsync();
            // CREATE DATABASE refuses to run inside the transaction a multi-statement command is.
            await using var create = server.CreateCommand();
            create.CommandText = $"CREATE DATABASE {PostgresDynamicTargetDatabase}";
            await create.ExecuteNonQueryAsync();
            // Revocation waits up to this plus a margin for a connection still starting up; two seconds keeps
            // the tests that make it wait short. ALTER SYSTEM also refuses a transaction.
            await using var shortenAuthentication = server.CreateCommand();
            shortenAuthentication.CommandText = "ALTER SYSTEM SET authentication_timeout = '2s'";
            await shortenAuthentication.ExecuteNonQueryAsync();
            await using var reload = server.CreateCommand();
            reload.CommandText = "SELECT pg_reload_conf()";
            await reload.ExecuteNonQueryAsync();
        }

        var targetAsSuperuser = new NpgsqlConnectionStringBuilder(_postgresTarget.GetConnectionString()) { Database = PostgresDynamicTargetDatabase };
        await using (var target = new NpgsqlConnection(targetAsSuperuser.ConnectionString))
        {
            await target.OpenAsync();
            await using var grant = target.CreateCommand();
            // What a recipe that lets its role create tables needs, as "whatever your role statements need" in INSTALL.md.
            grant.CommandText = $"GRANT CREATE ON SCHEMA public TO {PostgresTargetLogin} WITH GRANT OPTION";
            await grant.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_postgresTarget.GetConnectionString())
        {
            Database = PostgresDynamicTargetDatabase,
            Username = PostgresTargetLogin,
            Password = password,
        }.ConnectionString;
    }

    protected async ValueTask DisposePostgresTargetAsync()
    {
        if (_postgresTarget is not null)
        {
            await _postgresTarget.DisposeAsync();
        }
    }

    public abstract ValueTask DisposeAsync();

    protected static async Task<string> CreateDynamicTargetAsync(string serverConnectionString)
    {
        await using (var server = new SqlConnection(serverConnectionString))
        {
            await server.OpenAsync();
            await using var create = server.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{DynamicTargetDatabase}]";
            await create.ExecuteNonQueryAsync();
        }

        return new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = DynamicTargetDatabase }.ConnectionString;
    }

    protected static PostgreSqlContainer NewPostgresContainer(string engine) => new PostgreSqlBuilder(PostgresShareIssuerBackfill.PostgresImage)
        .WithName(ContainerName(engine))
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    private const string DynamicTargetDatabase = "DynamicTarget";

    private const string PostgresDynamicTargetDatabase = "dynamic_target";

    private const string PostgresTargetLogin = "eclipsvault_credmgr";

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

    // The vault's own server already runs SQL Server, so the target is a second database on it.
    public override Task<string> StartDynamicTargetAsync() => CreateDynamicTargetAsync(_sql.GetConnectionString());

    public override async ValueTask DisposeAsync()
    {
        await _sql.DisposeAsync();
        await DisposePostgresTargetAsync();
    }
}

internal sealed class PostgresVaultDatabase : VaultDatabase
{
    // The vault signs in as the server's superuser, as it signs in to SQL Server as sa, so this
    // password is per run too.
    private readonly PostgreSqlContainer _postgres = NewPostgresContainer("postgres");

    public override string Provider => DatabaseProvider.Postgres;

    private MsSqlContainer? _dynamicTarget;

    public override Task StartAsync() => _postgres.StartAsync();

    public override string ConnectionString()
        => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = "eclipsvault" }.ConnectionString;

    // Dynamic credentials are SQL Server logins, so a PostgreSQL vault needs a SQL Server beside it.
    public override async Task<string> StartDynamicTargetAsync()
    {
        _dynamicTarget = new MsSqlBuilder(VaultFixture.SqlServerImage)
            .WithName(ContainerName("sql-target"))
            .WithPassword("Sa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
            .Build();
        await _dynamicTarget.StartAsync();
        return await CreateDynamicTargetAsync(_dynamicTarget.GetConnectionString());
    }

    public override async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (_dynamicTarget is not null)
        {
            await _dynamicTarget.DisposeAsync();
        }
        await DisposePostgresTargetAsync();
    }
}
