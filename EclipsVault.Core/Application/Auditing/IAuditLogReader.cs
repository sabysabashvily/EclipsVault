using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Auditing;

public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string Username,
    string SourceIp,
    AuditAction Action,
    string ResourceType,
    string? ResourceName,
    string? Details,
    bool IsCritical);

/// <summary>Read-only access to the audit trail for dashboards and the audit viewer.</summary>
public interface IAuditLogReader
{
    /// <summary>Latest entries, newest first; optionally restricted to one actor.</summary>
    Task<IReadOnlyList<AuditEntryDto>> ListRecentAsync(int count, string? username, CancellationToken ct);

    /// <summary>
    /// A page of one actor's own entries, newest first, keyed by user id — the source for the
    /// personal activity feed. Filtering by id (not the mutable username) keeps the feed exact.
    /// </summary>
    Task<IReadOnlyList<AuditEntryDto>> ListForActorAsync(Guid actorUserId, int skip, int take, CancellationToken ct);

    /// <summary>
    /// The most recent entries for one actor whose action is in <paramref name="actions"/>, newest
    /// first, keyed by user id. The action filter is applied in the database so we never pull the
    /// whole feed to surface a narrow slice (e.g. just the sign-in events). Returns an empty list
    /// for an empty actor or an empty action set.
    /// </summary>
    Task<IReadOnlyList<AuditEntryDto>> ListForActorByActionsAsync(
        Guid actorUserId, IReadOnlyCollection<AuditAction> actions, int take, CancellationToken ct);

    Task<int> CountCriticalSinceAsync(DateTimeOffset sinceUtc, CancellationToken ct);
}
