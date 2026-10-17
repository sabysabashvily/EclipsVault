using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Activity;

/// <summary>
/// Projects a user's own slice of the immutable audit trail into a friendly, categorised
/// activity feed. Read-only and actor-scoped: it only ever returns rows the user themselves
/// generated, keyed by user id (never by the mutable display name), and pages without a
/// second count query by fetching one row beyond the page to detect whether more exist.
/// </summary>
public sealed class ActivityService : IActivityService
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    private readonly IAuditLogReader _audit;

    public ActivityService(IAuditLogReader audit) => _audit = audit;

    public async Task<ActivityFeed> GetForUserAsync(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        if (userId == Guid.Empty)
        {
            return ActivityFeed.Empty(DefaultPageSize);
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        // Computed in 64 bits: a huge page number would wrap a 32-bit offset into one the database
        // refuses. Clamped, any page beyond the last reads as empty instead.
        var skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        // One extra row tells us whether a further page exists — no separate COUNT round-trip.
        var rows = await _audit.ListForActorAsync(userId, skip, pageSize + 1, ct);
        var hasMore = rows.Count > pageSize;

        var items = rows
            .Take(pageSize)
            .Select(r =>
            {
                var d = ActivityDescriber.Describe(r.Action);
                // Account-unlock notifications belong to the affected account's history, but the
                // administrator's request address belongs only in the full audit trail.
                var sourceIp = r.Action == AuditAction.AccountUnlocked ? "internal" : r.SourceIp;
                return new ActivityItem(r.TimestampUtc, d.Category, d.Title, d.Severity, r.ResourceName, sourceIp);
            })
            .ToList();

        return new ActivityFeed(items, page, pageSize, hasMore);
    }
}
