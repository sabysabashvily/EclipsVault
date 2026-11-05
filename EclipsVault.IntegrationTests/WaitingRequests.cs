using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.IntegrationTests;

/// <summary>Watches the vault's database for requests waiting on a lock, on either engine.</summary>
internal static class WaitingRequests
{
    private const string SqlServerWaitingRequests = """
        SELECT COUNT(*) AS [Value] FROM sys.dm_exec_requests
        WHERE database_id = DB_ID() AND blocking_session_id <> 0
        """;

    // Count every waiting lock request, not only those this connection blocks: a second write can wait
    // behind the first for the audit chain. The vault's PostgreSQL server holds only its database.
    // pg_locks is read live, where pg_stat_activity would be read once per transaction.
    private const string PostgresWaitingRequests = """
        SELECT COUNT(DISTINCT pid)::int AS "Value" FROM pg_locks WHERE NOT granted
        """;

    /// <summary>
    /// Returns once at least <paramref name="count"/> requests wait on a lock; fails after ten seconds. A request
    /// may wait on a row another transaction holds, or behind another request for the audit chain.
    /// </summary>
    public static async Task AtLeastAsync(EclipsVaultDbContext db, int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waitingRequests = db.Database.IsNpgsql() ? PostgresWaitingRequests : SqlServerWaitingRequests;
        while (await db.Database.SqlQueryRaw<int>(waitingRequests).SingleAsync(deadline.Token) < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
        }
    }
}
