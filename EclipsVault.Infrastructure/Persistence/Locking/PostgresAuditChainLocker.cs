using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Locking;

/// <summary>
/// The chain lock on PostgreSQL. <c>pg_advisory_xact_lock</c> is the direct counterpart of
/// <c>sp_getapplock</c> with <c>@LockOwner = 'Transaction'</c> — the <c>xact</c> is the point: the
/// lock is released by the commit, so it spans exactly read-head → insert → commit and cannot
/// outlive or under-live the write it protects.
/// </summary>
public sealed class PostgresAuditChainLocker : IAuditChainLocker
{
    public async Task AcquireAsync(DbContext db, string resource, TimeSpan timeout, CancellationToken ct)
    {
        // pg_advisory_xact_lock waits forever by itself; lock_timeout is what turns that into a
        // failure instead of a hung request. SET LOCAL scopes it to this transaction, so the
        // timeout cannot leak onto the pooled connection's next user.
        var milliseconds = (int)timeout.TotalMilliseconds;

        // EF1002 flags interpolation into raw SQL, and is right to. SET LOCAL accepts no bind
        // parameter, so this one value cannot be passed as one — it is an int, formatted invariantly,
        // derived from a constant in this assembly. Nothing a caller controls reaches it.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            FormattableString.Invariant($"SET LOCAL lock_timeout = '{milliseconds}ms'"), ct);
#pragma warning restore EF1002

        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [KeyFor(resource)], ct);
    }

    /// <summary>
    /// Advisory locks are keyed by a 64-bit integer, so the resource name has to become one.
    ///
    /// It is hashed here rather than with PostgreSQL's <c>hashtext()</c> (undocumented, and free to
    /// change between versions), and emphatically not with <c>string.GetHashCode()</c>, which is
    /// randomised per process — every replica would hash the same name to a different key, take a
    /// different lock, and serialise against nobody. The chain would fork on day one, silently, and
    /// only under load. SHA-256 is fixed for all time, which is the only property that matters here.
    /// </summary>
    internal static long KeyFor(string resource)
        => BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(resource)), 0);
}
