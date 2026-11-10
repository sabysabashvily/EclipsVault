using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Locking;

/// <summary>The session lock on PostgreSQL: a session-level advisory lock, the counterpart of <c>pg_advisory_xact_lock</c>.</summary>
public sealed class PostgresSessionLocker : ISessionLocker
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(DbContext db, string resource, CancellationToken ct)
    {
        var key = PostgresAuditChainLocker.KeyFor(resource);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // Never cancelled: a caller leaving after the lock was granted would skip its release, and
            // pg_try_advisory_lock answers at once anyway.
            var taken = (await db.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock({0}) AS \"Value\"", key).ToListAsync(CancellationToken.None)).Single();
            if (taken)
            {
                return new SessionLock(db, () => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", [key], CancellationToken.None));
            }
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }

        await db.Database.CloseConnectionAsync();
        return null;
    }
}
