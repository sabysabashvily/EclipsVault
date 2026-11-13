namespace EclipsVault.Core.Application.Auditing;

/// <summary>
/// The time an audit row or checkpoint is stamped with, before it is hashed or signed. It is cut to
/// whole microseconds because PostgreSQL stores no finer, so the time read back is the time that was
/// hashed or signed; a Linux or Windows clock reads to 100 ns.
/// </summary>
public static class AuditClock
{
    public static DateTimeOffset GetAuditUtcNow(this TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.UtcTicks % TimeSpan.TicksPerMicrosecond));
    }
}
