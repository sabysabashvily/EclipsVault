using EclipsVault.Core.Domain.Enums;
using Npgsql;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// <c>Postgres</c> dynamic roles, added to the vault as an operator provisions them, and the vault's
/// PostgreSQL target read as that operator inspects it.
/// </summary>
internal static class PostgresDynamicTarget
{
    /// <summary>The Development seed's reader recipe: a login role that expires with its lease and reads every table.</summary>
    public const string ReaderRecipe = """
        CREATE ROLE "{{name}}" LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}+00' IN ROLE pg_read_all_data;
        """;

    /// <summary>The Development seed's revocation: ends the role's sessions and drops it, safe to run again.</summary>
    public const string RepeatableRevocation = """
        SELECT pg_terminate_backend(pid, 5000) FROM pg_stat_activity WHERE usename = '{{name}}';
        DO $$
        BEGIN
            IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{{name}}') THEN
                REASSIGN OWNED BY "{{name}}" TO CURRENT_USER;
                DROP OWNED BY "{{name}}";
            END IF;
        END
        $$;
        DROP ROLE IF EXISTS "{{name}}";
        """;

    /// <summary>A revocation the target always refuses.</summary>
    public const string RefusedRevocation = "DO $$ BEGIN RAISE EXCEPTION 'Revocation refused.'; END $$;";

    public static Task<Guid> AddRoleAsync(
        VaultFixture vault, string name, string creation = ReaderRecipe, string revocation = RepeatableRevocation)
        => DynamicSecretsApi.AddRoleAsync(vault, name, creation: creation, revocation: revocation, backend: DynamicSecretBackend.Postgres);

    private static string ConnectionString(VaultFixture vault) => vault.PostgresDynamicTargetConnectionString
        ?? throw new InvalidOperationException("This vault has no PostgreSQL target; set PostgresDynamicSecretsTarget.");

    /// <summary>The login roles the vault minted for a role.</summary>
    public static async Task<string[]> RolesOfRoleAsync(VaultFixture vault, string roleName)
    {
        await using var target = new NpgsqlConnection(ConnectionString(vault));
        await target.OpenAsync();
        await using var query = new NpgsqlCommand(@"SELECT rolname FROM pg_roles WHERE rolname LIKE @prefix", target);
        query.Parameters.AddWithValue("prefix", $@"ev\_{roleName.Replace("_", @"\_", StringComparison.Ordinal)}\_%");
        var roles = new List<string>();
        await using var rows = await query.ExecuteReaderAsync();
        while (await rows.ReadAsync())
        {
            roles.Add(rows.GetString(0));
        }
        return roles.ToArray();
    }

    /// <summary>When the role's password stops working, as the target records it.</summary>
    public static async Task<DateTimeOffset?> ValidUntilAsync(VaultFixture vault, string role)
    {
        await using var target = new NpgsqlConnection(ConnectionString(vault));
        await target.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT rolvaliduntil FROM pg_roles WHERE rolname = @role", target);
        query.Parameters.AddWithValue("role", role);
        return await query.ExecuteScalarAsync() is DateTime validUntil ? new DateTimeOffset(validUntil, TimeSpan.Zero) : null;
    }

    /// <summary>
    /// Opens a connection as a minted role, which proves its password works there. A refused password
    /// must not be served from a pool opened with it before, so the connection is never pooled.
    /// </summary>
    public static async Task<NpgsqlConnection> SignInAsync(VaultFixture vault, string username, string password)
    {
        var asRole = new NpgsqlConnectionStringBuilder(ConnectionString(vault)) { Username = username, Password = password, Pooling = false };
        var connection = new NpgsqlConnection(asRole.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Whether the target accepts <paramref name="password"/> for <paramref name="username"/> right now.</summary>
    public static async Task<bool> OpensWithAsync(VaultFixture vault, string username, string password)
    {
        try
        {
            await using var connection = await SignInAsync(vault, username, password);
            return true;
        }
        catch (PostgresException)
        {
            return false;
        }
    }

    /// <summary>Whether a connection opened earlier can still run a query.</summary>
    public static async Task<bool> StillWorksAsync(NpgsqlConnection connection)
    {
        try
        {
            await using var query = new NpgsqlCommand("SELECT 1", connection);
            return await query.ExecuteScalarAsync() is 1;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    /// <summary>Drops a role a test left on the target on purpose, so the next run starts clean.</summary>
    public static async Task DropRoleAsync(VaultFixture vault, string role)
    {
        await using var target = new NpgsqlConnection(ConnectionString(vault));
        await target.OpenAsync();
        // The name is the vault's minted identity: letters, digits and underscores only.
        await using var drop = new NpgsqlCommand($"""DROP ROLE IF EXISTS "{role}" """, target);
        await drop.ExecuteNonQueryAsync();
    }
}
