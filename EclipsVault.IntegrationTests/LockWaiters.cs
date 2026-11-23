using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Waits until a number of requests wait on the vault's named locks (an account's lock or the audit
/// chain), so a test knows the order they will be granted in. Both databases grant them first come, first served.
/// </summary>
internal static class LockWaiters
{
    private const string SqlServer = """
        SELECT COUNT(*) AS [Value] FROM sys.dm_tran_locks
        WHERE resource_type = 'APPLICATION' AND resource_database_id = DB_ID() AND request_status = 'WAIT'
        """;

    private const string Postgres = """
        SELECT COUNT(*)::int AS "Value" FROM pg_locks
        WHERE NOT granted AND locktype = 'advisory' AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
        """;

    public static async Task WaitForAsync(VaultFixture vault, int count)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var query = db.Database.IsNpgsql() ? Postgres : SqlServer;
        while (await db.Database.SqlQueryRaw<int>(query).SingleAsync(deadline.Token) < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
        }
    }
}
