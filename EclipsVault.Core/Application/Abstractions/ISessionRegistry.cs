namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Server-side registry of live interactive sessions ("signed-in devices"), so a user can see
/// where they are logged in and revoke an individual session — the per-session complement to the
/// blunt "sign out everywhere" kill switch (<see cref="ISessionRevocationService"/>). Shared
/// runtime state like the revocation marker and IP blacklist: an in-process store for a single
/// node, a Redis-backed store when multiple nodes must agree.
/// </summary>
public interface ISessionRegistry
{
    /// <summary>
    /// Records a sighting of a session: creates it on first sight (capturing device, IP, and
    /// created-at) and refreshes its last-seen thereafter. Throttled, so calling it on every
    /// request is cheap. Best-effort metadata — a failure here must never reject a valid session.
    /// </summary>
    Task RecordSeenAsync(SessionObservation observation, CancellationToken ct = default);

    /// <summary>The user's active (non-expired, non-revoked) sessions, most-recently-active first.</summary>
    Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Revokes a single session: it is rejected on its next request (a tombstone the cookie
    /// validator checks) and drops off the list. Scoped to the owning user, so a caller can only
    /// ever revoke their own sessions. Returns true only when a live session of that account was
    /// removed; an expired, unknown, already revoked or other account's session returns false.
    /// A tombstone is retained even without metadata, so signing out still rejects copied cookies.
    /// </summary>
    Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct = default);

    Task<bool> IsRevokedAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
}
