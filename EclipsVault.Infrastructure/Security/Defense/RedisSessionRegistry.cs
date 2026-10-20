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
    private const string UpsertScript = @"
if redis.call('EXISTS', KEYS[2]) == 1 then return 0 end
local existing = redis.call('HGET', KEYS[1], ARGV[1])
if existing then
    local ok, obj = pcall(cjson.decode, existing)
    if ok and (tonumber(ARGV[2]) - tonumber(obj.LastSeenUnix) >= tonumber(ARGV[6])) then
        obj.LastSeenUnix = tonumber(ARGV[2])
        obj.ExpiresUnix = tonumber(ARGV[5])
        obj.IpAddress = ARGV[4]
        redis.call('HSET', KEYS[1], ARGV[1], cjson.encode(obj))
    end
else
    local obj = { SessionId = ARGV[1], Device = ARGV[3], IpAddress = ARGV[4],
                  CreatedUnix = tonumber(ARGV[2]), LastSeenUnix = tonumber(ARGV[2]), ExpiresUnix = tonumber(ARGV[5]) }
    redis.call('HSET', KEYS[1], ARGV[1], cjson.encode(obj))
end
redis.call('EXPIRE', KEYS[1], ARGV[7])
return 1";

    // One script prevents a competing revoke or touch from separating the live check and removal.
    // Store the tombstone before deletion so a failed write cannot leave a session able to return.
    private const string RevokeScript = """
        local session = redis.call('HGET', KEYS[1], ARGV[1])
        if not session then return 0 end
        local stored = cjson.decode(session)
        local now = tonumber(redis.call('TIME')[1])
        if tonumber(stored.ExpiresUnix) <= now then
            redis.call('HDEL', KEYS[1], ARGV[1])
            return 0
        end
        redis.call('SET', KEYS[2], '1', 'EX', ARGV[2])
        return redis.call('HDEL', KEYS[1], ARGV[1])
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

    public async Task RecordSeenAsync(SessionObservation o, CancellationToken ct = default)
    {
        var ttlSeconds = Math.Max(60, (int)(o.ExpiresAtUtc - o.SeenAtUtc).TotalSeconds);
        var db = _redis.GetDatabase();
        await db.ScriptEvaluateAsync(
            UpsertScript,
            new RedisKey[] { SessionsKey(o.UserId), RevokedKey(o.UserId, o.SessionId) },
            new RedisValue[]
            {
                o.SessionId.ToString("N"),
                o.SeenAtUtc.ToUnixTimeSeconds(),
                o.Device,
                o.IpAddress,
                o.ExpiresAtUtc.ToUnixTimeSeconds(),
                TouchThrottleSeconds,
                ttlSeconds
            });
    }

    public async Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync(SessionsKey(userId));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var result = new List<ActiveSession>(entries.Length);
        foreach (var entry in entries)
        {
            var stored = JsonSerializer.Deserialize<StoredSession>((string)entry.Value!, Json);
            if (stored is null || stored.ExpiresUnix <= now || !Guid.TryParse(stored.SessionId, out var sid))
            {
                continue;
            }

            result.Add(new ActiveSession(
                sid,
                stored.Device,
                stored.IpAddress,
                DateTimeOffset.FromUnixTimeSeconds(stored.CreatedUnix),
                DateTimeOffset.FromUnixTimeSeconds(stored.LastSeenUnix)));
        }

        return result.OrderByDescending(s => s.LastSeenAtUtc).ToList();
    }

    public async Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var removed = (long)await db.ScriptEvaluateAsync(
            RevokeScript,
            new RedisKey[] { SessionsKey(userId), RevokedKey(userId, sessionId) },
            new RedisValue[] { sessionId.ToString("N"), _tombstoneSeconds }) == 1;
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
