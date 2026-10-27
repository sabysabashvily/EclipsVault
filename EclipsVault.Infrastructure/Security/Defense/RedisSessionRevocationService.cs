using System.Globalization;
using EclipsVault.Infrastructure.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// Redis-backed session revocation versions, one key per account, read by cookie validation on
/// every node so a revocation raised anywhere takes effect everywhere. Each read extends the key's
/// retention, so it outlives every session still in use; once it expires or is evicted, the sessions
/// that carried it are refused.
/// </summary>
public sealed class RedisSessionRevocationService : ISessionRevocationService
{
    // One script so two first sign-ins of an account cannot each create, and keep, a version of their own.
    private const string CaptureScript = """
        redis.call('SET', KEYS[1], ARGV[1], 'NX', 'EX', ARGV[2])
        return redis.call('GETEX', KEYS[1], 'EX', ARGV[2])
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly string _prefix;
    private readonly TimeSpan _retention;
    private readonly ILogger<RedisSessionRevocationService> _logger;

    public RedisSessionRevocationService(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> options,
        ILogger<RedisSessionRevocationService> logger)
    {
        _redis = redis;
        _prefix = options.Value.SessionKeyPrefix;
        _retention = TimeSpan.FromHours(Math.Max(1, options.Value.RevocationRetentionHours));
        _logger = logger;
    }

    public async Task<string> CaptureAsync(Guid userId, CancellationToken ct = default)
    {
        var version = await _redis.GetDatabase().ScriptEvaluateAsync(
            CaptureScript,
            [Key(userId)],
            [NewVersion(), (long)_retention.TotalSeconds]);
        return (string)version!;
    }

    public async Task<string?> FindAsync(Guid userId, CancellationToken ct = default)
        => (string?)await _redis.GetDatabase().StringGetSetExpiryAsync(Key(userId), _retention);

    public async Task RevokeAsync(Guid userId, CancellationToken ct = default)
    {
        await _redis.GetDatabase().StringSetAsync(Key(userId), NewVersion(), _retention);
        _logger.LogWarning("Every session of user {UserId} was revoked", userId);
    }

    private static string NewVersion() => Guid.NewGuid().ToString("N");

    private RedisKey Key(Guid userId)
        => string.Create(CultureInfo.InvariantCulture, $"{_prefix}:revocation-version:{userId:N}");
}
