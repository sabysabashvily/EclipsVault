using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Locking;

/// <summary>The session lock on SQL Server: <c>sp_getapplock</c> owned by the session, not a transaction.</summary>
public sealed class SqlServerSessionLocker : ISessionLocker
{
    // A zero timeout answers at once: 0 when granted, -1 when another session holds the lock.
    private const string AcquireSql = """
        DECLARE @result int;
        EXEC @result = sp_getapplock
            @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
        SELECT @result AS [Value];
        """;

    // A pooled connection keeps its session, so the lock is released here rather than by closing.
    private const string ReleaseSql = "EXEC sp_releaseapplock @Resource = @p0, @LockOwner = 'Session';";

    public async Task<IAsyncDisposable?> TryAcquireAsync(DbContext db, string resource, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // Not composed with Single(): EF would wrap the batch in a subquery, which a DECLARE cannot be.
            var result = (await db.Database.SqlQueryRaw<int>(AcquireSql, resource).ToListAsync(ct)).Single();
            if (result >= 0)
            {
                return new SessionLock(db, () => db.Database.ExecuteSqlRawAsync(ReleaseSql, [resource], CancellationToken.None));
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
