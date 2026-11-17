using System.Net;
using System.Text.RegularExpressions;
using EclipsVault.Core.Application.DynamicSecrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Legacy Dynamic secrets page as a browser drives it, and the vault's target database as an
/// operator inspects it. A production vault has no screen that creates roles, so tests add them to
/// its database, as an operator provisions them out of band.
/// </summary>
internal static partial class LegacyDynamicSecrets
{
    public const string Page = "/DynamicSecrets";

    /// <summary>The Development seed's reader recipe: a login, its user, and read access.</summary>
    public const string ReaderRecipe = """
        CREATE LOGIN [{{name}}] WITH PASSWORD = '{{password}}';
        CREATE USER [{{name}}] FOR LOGIN [{{name}}];
        ALTER ROLE db_datareader ADD MEMBER [{{name}}];
        """;

    /// <summary>The Development seed's revocation: safe to run again once the login is gone.</summary>
    public const string RepeatableRevocation = """
        DECLARE @sessions nvarchar(max) = N'';
        SELECT @sessions = @sessions + N'KILL ' + CAST(session_id AS nvarchar(10)) + N';'
        FROM sys.dm_exec_sessions WHERE login_name = N'{{name}}';
        IF LEN(@sessions) > 0 EXEC sp_executesql @sessions;
        DROP USER IF EXISTS [{{name}}];
        IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{{name}}') DROP LOGIN [{{name}}];
        """;

    /// <summary>An operator-written revocation that fails when run a second time.</summary>
    public const string OneShotRevocation = """
        DROP USER [{{name}}];
        DROP LOGIN [{{name}}];
        """;

    public static async Task<Guid> AddRoleAsync(
        VaultFixture vault, string name, SensitivityLevel sensitivity = SensitivityLevel.Internal,
        string creation = ReaderRecipe, string revocation = RepeatableRevocation, int defaultTtlMinutes = 15, int maxTtlMinutes = 60)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var role = new DynamicSecretRole
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Integration test role.",
            ProjectKey = "GLOBAL",
            Environment = SecretEnvironment.Development,
            Sensitivity = sensitivity,
            Backend = DynamicSecretBackend.SqlServer,
            CreationStatements = creation,
            RevocationStatements = revocation,
            DefaultTtlMinutes = defaultTtlMinutes,
            MaxTtlMinutes = maxTtlMinutes,
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.DynamicSecretRoles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    /// <summary>The page's antiforgery token. It belongs to the session, so one serves every form on it.</summary>
    public static async Task<string> TokenAsync(HttpClient client)
        => VaultFixture.FormTokenOf(await client.GetStringAsync(Page), Page);

    public static async Task<HttpResponseMessage> IssueAsync(HttpClient client, Guid roleId, int? ttlMinutes = null, string? token = null)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token ?? await TokenAsync(client),
            ["RoleId"] = roleId.ToString(),
        };
        if (ttlMinutes is { } minutes)
        {
            form["TtlMinutes"] = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return await client.PostAsync($"{Page}/Issue", new FormUrlEncodedContent(form));
    }

    public static async Task<HttpResponseMessage> RevokeAsync(HttpClient client, Guid leaseId, string? token = null)
        => await client.PostAsync($"{Page}/Revoke", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token ?? await TokenAsync(client),
            ["id"] = leaseId.ToString(),
        }));

    /// <summary>The message the page shows after a refused or completed action redirects back to it.</summary>
    public static async Task<string> FlashAsync(HttpClient client)
        => FlashMessage().Match(await client.GetStringAsync(Page)) is { Success: true } match
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException("The page showed no message.");

    /// <summary>The username and password the page shows once, after issuing.</summary>
    public static async Task<(string Username, string Password)> IssuedCredentialAsync(HttpResponseMessage issued)
    {
        var page = await issued.Content.ReadAsStringAsync();
        return (IssuedField("Username").Match(page).Groups[1].Value, IssuedField("Password").Match(page).Groups[1].Value);
    }

    public static async Task<DynamicSecretLease[]> LeasesOfRoleAsync(VaultFixture vault, Guid roleId)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.DynamicSecretLeases.AsNoTracking().Where(lease => lease.RoleId == roleId).ToArrayAsync();
    }

    /// <summary>
    /// Moves a lease's expiry. The suite cannot wait out a lease, so it changes the date the sweep reads;
    /// the change bypasses the vault, so it writes no audit row.
    /// </summary>
    public static async Task ExpireAtAsync(VaultFixture vault, Guid leaseId, DateTimeOffset expiresAtUtc)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.DynamicSecretLeases.Where(lease => lease.Id == leaseId)
            .ExecuteUpdateAsync(lease => lease.SetProperty(l => l.ExpiresAtUtc, expiresAtUtc));
    }

    /// <summary>Runs the lease sweep once, as the worker does on its timer.</summary>
    public static async Task<int> SweepAsync(VaultFixture vault)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDynamicSecretService>().ReapDueLeasesAsync(CancellationToken.None);
    }

    /// <summary>The logins the vault minted for a role, read on the target as its operator would.</summary>
    public static async Task<string[]> LoginsOfRoleAsync(VaultFixture vault, string roleName)
    {
        await using var target = new SqlConnection(vault.DynamicTargetConnectionString);
        await target.OpenAsync();
        await using var query = target.CreateCommand();
        query.CommandText = "SELECT name FROM sys.server_principals WHERE name LIKE @prefix";
        query.Parameters.AddWithValue("@prefix", $"ev[_]{roleName.Replace("_", "[_]", StringComparison.Ordinal)}[_]%");
        var logins = new List<string>();
        await using var rows = await query.ExecuteReaderAsync();
        while (await rows.ReadAsync())
        {
            logins.Add(rows.GetString(0));
        }
        return logins.ToArray();
    }

    /// <summary>Signs in to the target as a minted login, which proves its password works there.</summary>
    public static async Task<int> SignInToTargetAsync(VaultFixture vault, string username, string password)
    {
        var builder = new SqlConnectionStringBuilder(vault.DynamicTargetConnectionString) { UserID = username, Password = password };
        await using var target = new SqlConnection(builder.ConnectionString);
        await target.OpenAsync();
        await using var query = target.CreateCommand();
        query.CommandText = "SELECT 1";
        return (int)(await query.ExecuteScalarAsync())!;
    }

    /// <summary>Drops a login a test left on the target on purpose, so the next run starts clean.</summary>
    public static async Task DropLoginAsync(VaultFixture vault, string login)
    {
        await using var target = new SqlConnection(vault.DynamicTargetConnectionString);
        await target.OpenAsync();
        await using var drop = target.CreateCommand();
        drop.CommandText = $"DROP USER IF EXISTS [{login}]; DROP LOGIN [{login}];";
        await drop.ExecuteNonQueryAsync();
    }

    [GeneratedRegex("<div class=\"flash [^\"]+\" data-flash role=\"status\">\\s*<span>([^<]*)</span>")]
    private static partial Regex FlashMessage();

    private static Regex IssuedField(string label) => new($"<th scope=\"row\">{label}</th><td><code class=\"mono\">([^<]+)</code></td>");
}
