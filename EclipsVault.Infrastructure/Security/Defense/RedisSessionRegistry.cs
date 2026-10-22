using System.Globalization;
using System.Text.Json;
using EclipsVault.Infrastructure.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// Redis-backed session registry so every node sees the same signed-in devices and an individual
/// revocation raised on one node is honoured on all. Each user's sessions live in one hash
/// (field = session id → JSON), and a revoked session is a short-lived tombstone key the cookie
/// validator checks. An atomic Lua upsert creates a session on first sight and throttles the
/// last-seen refresh, so the per-request touch is a single round-trip and never resurrects a
/// revoked session.
/// </summary>
public sealed class RedisSessionRegistry : ISessionRegistry
{
    private const string UpsertScript = """
        local sessionsKey, tombstoneKey = KEYS[1], KEYS[2]
        local sessionField, seenUnix, device, ipAddress = ARGV[1], tonumber(ARGV[2]), ARGV[3], ARGV[4]
        local expiresUnix, throttleSeconds, ttlSeconds = tonumber(ARGV[5]), tonumber(ARGV[6]), ARGV[7]
        if redis.call('EXISTS', tombstoneKey) == 1 then return 0 end
        local existing = redis.call('HGET', sessionsKey, sessionField)
        if existing then
            local ok, stored = pcall(cjson.decode, existing)
            if ok and (seenUnix - tonumber(stored.LastSeenUnix) >= throttleSeconds) then
                stored.LastSeenUnix = seenUnix
                stored.ExpiresUnix = expiresUnix
                stored.IpAddress = ipAddress
                redis.call('HSET', sessionsKey, sessionField, cjson.encode(stored))
            end
        else
            local stored = { SessionId = sessionField, Device = device, IpAddress = ipAddress,
                             CreatedUnix = seenUnix, LastSeenUnix = seenUnix, ExpiresUnix = expiresUnix }
            redis.call('HSET', sessionsKey, sessionField, cjson.encode(stored))
        end
        redis.call('EXPIRE', sessionsKey, ttlSeconds)
        return 1
        """;

    // One script prevents a competing revoke or touch from separating the live check and removal.
    // Store the tombstone before deletion so a failed write cannot leave a session able to return.
    // Missing metadata does not prove that the cookie has expired, so even a miss gets a tombstone.
    private const string RevokeScript = """
        local sessionsKey, tombstoneKey = KEYS[1], KEYS[2]
        local sessionField, tombstoneSeconds = ARGV[1], ARGV[2]
        local alreadyRevoked = redis.call('EXISTS', tombstoneKey)
        local session = redis.call('HGET', sessionsKey, sessionField)
        redis.call('SET', tombstoneKey, '1', 'EX', tombstoneSeconds)
        local removed = redis.call('HDEL', sessionsKey, sessionField)
        if alreadyRevoked == 1 or not session then return 0 end
        local stored = cjson.decode(session)
        local now = tonumber(redis.call('TIME')[1])
        if tonumber(stored.ExpiresUnix) <= now then return 0 end
        return removed
        """;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private sealed record StoredSession(
        string SessionId, string Device, string IpAddress, long CreatedUnix, long LastSeenUnix, long ExpiresUnix);

    private readonly IConnectionMultiplexer _redis;
    private readonly string _prefix;
    private readonly int _tombstoneSeconds;
    private readonly ILogger<RedisSessionRegistry> _logger;

    // Only refresh last-seen once it has drifted by this much — throttles per-request writes.
    private const int TouchThrottleSeconds = 60;

    public RedisSessionRegistry(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> options,
        ILogger<RedisSessionRegistry> logger)
    {
        _redis = redis;
        _prefix = options.Value.InstanceName;
        // A tombstone must outlive any live cookie; reuse the revocation retention (default 24h).
        _tombstoneSeconds = Math.Max(1, options.Value.RevocationRetentionHours) * 3600;
        _logger = logger;
    }

    public async Task RecordSeenAsync(SessionObservation observation, CancellationToken ct = default)
    {
        var ttlSeconds = Math.Max(60, (int)(observation.ExpiresAtUtc - observation.SeenAtUtc).TotalSeconds);
        var database = _redis.GetDatabase();
        await database.ScriptEvaluateAsync(
            UpsertScript,
            new RedisKey[] { SessionsKey(observation.UserId), RevokedKey(observation.UserId, observation.SessionId) },
            new RedisValue[]
            {
                observation.SessionId.ToString("N"),
                observation.SeenAtUtc.ToUnixTimeSeconds(),
                observation.Device,
                observation.IpAddress,
                observation.ExpiresAtUtc.ToUnixTimeSeconds(),
                TouchThrottleSeconds,
                ttlSeconds
            });
    }

    public async Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var database = _redis.GetDatabase();
        var entries = await database.HashGetAllAsync(SessionsKey(userId));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var result = new List<ActiveSession>(entries.Length);
        foreach (var entry in entries)
        {
            var stored = JsonSerializer.Deserialize<StoredSession>((string)entry.Value!, Json);
            if (stored is null || stored.ExpiresUnix <= now || !Guid.TryParse(stored.SessionId, out var sessionId))
            {
                continue;
            }

            result.Add(new ActiveSession(
                sessionId,
                stored.Device,
                stored.IpAddress,
                DateTimeOffset.FromUnixTimeSeconds(stored.CreatedUnix),
                DateTimeOffset.FromUnixTimeSeconds(stored.LastSeenUnix)));
        }

        return result.OrderByDescending(s => s.LastSeenAtUtc).ToList();
    }

    public async Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var database = _redis.GetDatabase();
        var result = await database.ScriptEvaluateAsync(
            RevokeScript,
            new RedisKey[] { SessionsKey(userId), RevokedKey(userId, sessionId) },
            new RedisValue[] { sessionId.ToString("N"), _tombstoneSeconds });
        var removed = (long)result == 1;
        if (removed)
        {
            _logger.LogInformation("Session {SessionId} for user {UserId} was revoked", sessionId, userId);
        }

        return removed;
    }

    public async Task<bool> IsRevokedAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
        => await _redis.GetDatabase().KeyExistsAsync(RevokedKey(userId, sessionId));

    private RedisKey SessionsKey(Guid userId) => $"{_prefix}:sessions:{userId:N}";
    private RedisKey RevokedKey(Guid userId, Guid sessionId)
        => string.Create(CultureInfo.InvariantCulture, $"{_prefix}:session-revoked:{userId:N}:{sessionId:N}");
}
