using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence;

/// <summary>
/// Which database the vault runs on, selected by <c>Database:Provider</c>.
///
/// Startup requires the setting. There is no default: guessing SQL Server would hide a PostgreSQL
/// install, and guessing PostgreSQL would point an upgraded SQL Server install at the wrong engine.
/// PostgreSQL is supported because a licensed database is a line item on every self-hosted
/// deployment, and the vault should not be the reason anyone pays one. Only two things ever
/// depended on the engine: the chain lock (now behind <see cref="Locking.IAuditChainLocker"/>) and
/// the partial index predicate's quoting.
/// </summary>
public static class DatabaseProvider
{
    public const string SqlServer = "SqlServer";
    public const string Postgres = "Postgres";

    /// <summary>
    /// Migrations are generated per provider — the DDL differs down to the column types — so each
    /// engine needs its own set, and EF finds a set by assembly. SQL Server's live in Infrastructure
    /// where they have always been (moving them would rewrite history for every existing
    /// deployment); PostgreSQL's get their own assembly.
    /// </summary>
    public const string PostgresMigrationsAssembly = "EclipsVault.Migrations.Postgres";

    public static bool IsPostgres(string provider) =>
        provider.Equals(Postgres, StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("Npgsql", StringComparison.OrdinalIgnoreCase);

    private static bool IsSqlServer(string provider) =>
        provider.Equals(SqlServer, StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("MSSQL", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Points the context at the configured engine. An unrecognised name fails here rather than
    /// quietly falling back — silently starting on a database nobody chose is how a vault ends up
    /// writing secrets somewhere its operator is not looking.
    /// </summary>
    public static DbContextOptionsBuilder UseVaultDatabase(
        this DbContextOptionsBuilder options, string provider, string connectionString)
    {
        if (IsPostgres(provider))
        {
            return options.UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsAssembly(PostgresMigrationsAssembly));
        }

        if (IsSqlServer(provider))
        {
            return options.UseSqlServer(connectionString);
        }

        throw new InvalidOperationException(
            $"Database:Provider '{provider}' is not a database this vault knows how to run on. " +
            $"Use '{SqlServer}' or '{Postgres}'.");
    }
}
