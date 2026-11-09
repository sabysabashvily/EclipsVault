using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Locking;

/// <summary>
/// Takes a cluster-wide lock owned by a database session rather than a transaction, for work that
/// runs several transactions on one connection and must stay alone across all of them.
///
/// <para>The database releases the lock when the session ends, so it lasts exactly as long as its
/// holder: nothing expires it while the holder is still working, which rules out a lease (see
/// <see cref="IAuditChainLocker"/>), and a process that dies mid-work drops its connection and with
/// it the lock, so a crash never leaves the work refused until someone clears it by hand.</para>
/// </summary>
public interface ISessionLocker
{
    /// <summary>
    /// Opens <paramref name="db"/>'s connection and takes the lock on it without waiting. Returns the
    /// handle that releases the lock and closes the connection, or null, with the connection closed,
    /// when another session holds the lock.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(DbContext db, string resource, CancellationToken ct);
}

/// <summary>A held session lock. Releasing it never takes the caller's token: a cancelled holder must still let go.</summary>
internal sealed class SessionLock(DbContext db, Func<Task> release) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        try
        {
            await release();
        }
        catch (DbException)
        {
            // Either the session is gone, and the lock with it, or the locker has retired the session
            // so it cannot be reused while holding the lock. Throwing here would only replace the error
            // that ended the holder's work, such as an audit failure, with this one.
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
