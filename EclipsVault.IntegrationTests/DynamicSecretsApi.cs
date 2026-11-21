using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.DynamicSecrets;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Dynamic secrets UI API as the Angular page calls it, and the vault's target database as an
/// operator inspects it. A production vault has no screen that creates roles, so tests add them to
/// its database, as an operator provisions them out of band.
/// </summary>
internal static class DynamicSecretsApi
{
    public const string Path = "/api/ui/dynamic-secrets";

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
        string creation = ReaderRecipe, string revocation = RepeatableRevocation, int defaultTtlMinutes = 15, int maxTtlMinutes = 60,
        string project = "GLOBAL", SecretEnvironment environment = SecretEnvironment.Development, bool enabled = true)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var role = new DynamicSecretRole
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Integration test role.",
            ProjectKey = project,
            Environment = environment,
            Sensitivity = sensitivity,
            Backend = DynamicSecretBackend.SqlServer,
            CreationStatements = creation,
            RevocationStatements = revocation,
            DefaultTtlMinutes = defaultTtlMinutes,
            MaxTtlMinutes = maxTtlMinutes,
            IsEnabled = enabled,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.DynamicSecretRoles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    /// <summary>The session's antiforgery token, which serves every write it makes.</summary>
    public static Task<string> TokenAsync(HttpClient client) => UiApiRequest.AntiforgeryTokenAsync(client);

    public static async Task<JsonElement> GetAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore, "The response is not no-store");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<HttpResponseMessage> IssueAsync(HttpClient client, Guid roleId, int? ttlMinutes = null, string? token = null)
        => await UiApiRequest.SendWithTokenAsync(client, token ?? await TokenAsync(client), HttpMethod.Post, $"{Path}/issue",
            JsonContent.Create(new { roleId, ttlMinutes }));

    public static async Task<HttpResponseMessage> RevokeAsync(HttpClient client, Guid leaseId, string? token = null)
        => await UiApiRequest.SendWithTokenAsync(client, token ?? await TokenAsync(client), HttpMethod.Post, $"{Path}/leases/{leaseId}/revoke");

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A refused call's problem, carrying nothing beyond the members the contract names.</summary>
    public static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "The response is not no-store");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(problem.EnumerateObject(), member => Assert.Contains(member.Name, ProblemMembers));
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    private static readonly string[] ProblemMembers = ["code", "errors", "proof", "status", "title", "type"];

    /// <summary>The username and password the issue response carries, once.</summary>
    public static async Task<(string Username, string Password)> IssuedCredentialAsync(HttpResponseMessage issued)
    {
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var credential = await issued.Content.ReadFromJsonAsync<JsonElement>();
        return (credential.GetProperty("username").GetString()!, credential.GetProperty("password").GetString()!);
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

    /// <summary>
    /// Opens the dynamic-secrets evaluation window at a past moment. The suite cannot wait 30 days, so it
    /// writes the start the licence gate reads; the change bypasses the vault, so it writes no audit row.
    /// </summary>
    public static async Task StartEvaluationAtAsync(VaultFixture vault, DateTimeOffset startedAtUtc)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        db.FeatureEvaluations.Add(new FeatureEvaluation { FeatureKey = LicenseFeatures.DynamicSecrets, StartedAtUtc = startedAtUtc });
        await db.SaveChangesAsync();
    }

    public static async Task RemoveEvaluationAsync(VaultFixture vault)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.FeatureEvaluations.Where(evaluation => evaluation.FeatureKey == LicenseFeatures.DynamicSecrets).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Stores <paramref name="count"/> Active leases on a role, a minute apart from <paramref name="firstIssuedAtUtc"/>,
    /// and returns their ids oldest first. Far-future dates make them the newest in the vault and keep the sweep off them.
    /// </summary>
    public static async Task<Guid[]> StoreLeasesAsync(VaultFixture vault, Guid roleId, string roleName, int count, DateTimeOffset firstIssuedAtUtc)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var leases = Enumerable.Range(0, count).Select(minute => new DynamicSecretLease
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            RoleName = roleName,
            UserId = Guid.NewGuid(),
            Username = "it-dyn-newest",
            CredentialIdentity = $"ev_{roleName}_{minute:D2}",
            IssuedAtUtc = firstIssuedAtUtc.AddMinutes(minute),
            ExpiresAtUtc = firstIssuedAtUtc.AddMinutes(minute + 60),
            Status = LeaseStatus.Active,
        }).ToArray();
        db.DynamicSecretLeases.AddRange(leases);
        await db.SaveChangesAsync();
        return leases.Select(lease => lease.Id).ToArray();
    }

    /// <summary>Deletes a role's leases without the vault, so they leave no audit row and no sweep work.</summary>
    public static async Task DeleteLeasesAsync(VaultFixture vault, Guid roleId)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.DynamicSecretLeases.Where(lease => lease.RoleId == roleId).ExecuteDeleteAsync();
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
}
