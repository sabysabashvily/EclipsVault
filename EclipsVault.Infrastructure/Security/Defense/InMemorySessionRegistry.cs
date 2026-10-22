using Microsoft.Extensions.Logging;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// Tracks live sessions per user in process memory. Suitable for a single node; multi-node
/// deployments use the Redis-backed registry so every node sees the same sessions and a
/// revocation on one node is honoured on all. Revoked sessions are held as tombstones so a
/// lingering cookie is rejected and cannot resurrect its session by simply being seen again.
/// </summary>
public sealed class InMemorySessionRegistry : ISessionRegistry
{
    // Only rewrite last-seen once it has drifted by this much — keeps the per-request touch trivial.
    private static readonly TimeSpan TouchThrottle = TimeSpan.FromSeconds(60);
    // A tombstone older than any session's lifetime is moot (the cookie has expired anyway).
    private static readonly TimeSpan TombstoneRetention = TimeSpan.FromHours(12);
    private static readonly TimeSpan TombstoneSweepInterval = TimeSpan.FromMinutes(1);

    private sealed record Entry(
        Guid SessionId, string Device, string IpAddress,
        DateTimeOffset CreatedAtUtc, DateTimeOffset LastSeenAtUtc, DateTimeOffset ExpiresAtUtc);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Dictionary<Guid, Entry>> _sessions = new();
    private readonly Dictionary<(Guid UserId, Guid SessionId), DateTimeOffset> _revokedAt = new();
    private readonly ILogger<InMemorySessionRegistry> _logger;
    private readonly TimeProvider _clock;
    private DateTimeOffset _nextTombstoneSweepAt = DateTimeOffset.MinValue;

    public InMemorySessionRegistry(ILogger<InMemorySessionRegistry> logger, TimeProvider clock)
    {
        _logger = logger;
        _clock = clock;
    }

    public Task RecordSeenAsync(SessionObservation observation, CancellationToken ct = default)
    {
        lock (_gate)
        {
            // Recording and revoking share a lock so a racing request cannot resurrect a session.
            if (_revokedAt.ContainsKey((observation.UserId, observation.SessionId)))
            {
                return Task.CompletedTask;
            }

            if (!_sessions.TryGetValue(observation.UserId, out var sessions))
            {
                sessions = new Dictionary<Guid, Entry>();
                _sessions.Add(observation.UserId, sessions);
            }

            if (!sessions.TryGetValue(observation.SessionId, out var existing))
            {
                sessions.Add(observation.SessionId, new Entry(
                    observation.SessionId, observation.Device, observation.IpAddress,
                    observation.SeenAtUtc, observation.SeenAtUtc, observation.ExpiresAtUtc));
            }
            else if (observation.SeenAtUtc - existing.LastSeenAtUtc >= TouchThrottle)
            {
                sessions[observation.SessionId] = existing with
                {
                    LastSeenAtUtc = observation.SeenAtUtc,
                    ExpiresAtUtc = observation.ExpiresAtUtc,
                    IpAddress = observation.IpAddress
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(userId, out var sessions))
            {
                return Task.FromResult<IReadOnlyList<ActiveSession>>([]);
            }

            var now = _clock.GetUtcNow();
            // Materialize before removing so enumeration never observes a changing dictionary.
            foreach (var expired in sessions.Values.Where(entry => entry.ExpiresAtUtc <= now).ToList())
            {
                sessions.Remove(expired.SessionId);
            }

            IReadOnlyList<ActiveSession> list = sessions.Values
                .OrderByDescending(entry => entry.LastSeenAtUtc)
                .Select(entry => new ActiveSession(
                    entry.SessionId, entry.Device, entry.IpAddress, entry.CreatedAtUtc, entry.LastSeenAtUtc))
                .ToList();
            return Task.FromResult(list);
        }
    }

    public Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            SweepStaleTombstones(now);
            // Metadata can be missing while a cookie is valid; sign-out must still reject its copies.
            _revokedAt[(userId, sessionId)] = now;
            if (!_sessions.TryGetValue(userId, out var sessions) ||
                !sessions.Remove(sessionId, out var removed) || removed.ExpiresAtUtc <= now)
            {
                return Task.FromResult(false);
            }
        }

        _logger.LogInformation("Session {SessionId} for user {UserId} was revoked", sessionId, userId);
        return Task.FromResult(true);
    }

    private void SweepStaleTombstones(DateTimeOffset now)
    {
        // Unknown ids also create tombstones; repeated revokes must not repeatedly scan under the shared lock.
        if (now < _nextTombstoneSweepAt)
        {
            return;
        }

        _nextTombstoneSweepAt = now + TombstoneSweepInterval;
        var stale = _revokedAt.Where(entry => now - entry.Value > TombstoneRetention).ToList();
        foreach (var entry in stale)
        {
            _revokedAt.Remove(entry.Key);
        }
    }

    public Task<bool> IsRevokedAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_revokedAt.ContainsKey((userId, sessionId)));
        }
    }
}
