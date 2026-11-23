using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Npgsql;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// <c>Postgres</c> dynamic roles, added to the vault as an operator provisions them, and the vault's
/// PostgreSQL target read as that operator inspects it. Roles default to the Development seed's own
/// statements, so the tests exercise the recipes the vault ships.
/// </summary>
internal static class PostgresDynamicTarget
{
    /// <summary>A revocation that only drops the role, leaving its sessions to the vault.</summary>
    public const string DropOnlyRevocation = """DROP ROLE IF EXISTS "{{name}}";""";

    /// <summary>A revocation the target always refuses.</summary>
    public const string RefusedRevocation = "DO $$ BEGIN RAISE EXCEPTION 'Revocation refused.'; END $$;";

    public static Task<Guid> AddRoleAsync(
        VaultFixture vault, string name, string creation = DbSeeder.PostgresReaderRecipe, string revocation = DbSeeder.PostgresRevocation)
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

    /// <summary>
    /// Starts signing in as a minted role with its startup held for <paramref name="delay"/> after the
    /// sign-in check (<c>post_auth_delay</c>, which any client may ask for), so the connection has passed the
    /// check but is not yet listed in <c>pg_stat_activity</c>.
    /// </summary>
    public static Task<NpgsqlConnection> StartSigningInAsync(VaultFixture vault, string username, string password, TimeSpan delay)
    {
        var asRole = new NpgsqlConnectionStringBuilder(ConnectionString(vault))
        {
            Username = username,
            Password = password,
            Pooling = false,
            Options = $"-c post_auth_delay={(int)delay.TotalSeconds}",
            Timeout = 60,
        };
        return OpenAsync(new NpgsqlConnection(asRole.ConnectionString));
    }

    private static async Task<NpgsqlConnection> OpenAsync(NpgsqlConnection connection)
    {
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Waits until the target holds a startup transaction whose process it does not list yet.</summary>
    public static async Task WaitForUnlistedStartupAsync(VaultFixture vault)
    {
        await using var target = new NpgsqlConnection(ConnectionString(vault));
        await target.OpenAsync();
        await using var query = new NpgsqlCommand("""
            SELECT count(*) FROM pg_locks
            WHERE locktype = 'virtualxid' AND granted AND pid NOT IN (SELECT pid FROM pg_stat_activity)
            """, target);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((long)(await query.ExecuteScalarAsync())! > 0)
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new InvalidOperationException("No connection started up within ten seconds.");
    }

    /// <summary>Whether the target lets the role sign in, or null when there is no such role.</summary>
    public static async Task<bool?> CanSignInAsync(VaultFixture vault, string role)
    {
        await using var target = new NpgsqlConnection(ConnectionString(vault));
        await target.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT rolcanlogin FROM pg_roles WHERE rolname = @role", target);
        query.Parameters.AddWithValue("role", role);
        return await query.ExecuteScalarAsync() as bool?;
    }

    /// <summary>Whether a connection that was still signing in ended up with a session that runs queries.</summary>
    public static async Task<bool> EndsUpWorkingAsync(Task<NpgsqlConnection> signingIn)
    {
        try
        {
            await using var connection = await signingIn;
            return await StillWorksAsync(connection);
        }
        catch (NpgsqlException)
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

    /// <summary>The role that owns a table on the target, or null when there is no such table.</summary>
    public static async Task<string?> TableOwnerAsync(VaultFixture vault, string table)
    {
        await using var target = new NpgsqlConnection(ConnectionString(vault));
        await target.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT tableowner FROM pg_tables WHERE tablename = @table", target);
        query.Parameters.AddWithValue("table", table);
        return await query.ExecuteScalarAsync() as string;
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
