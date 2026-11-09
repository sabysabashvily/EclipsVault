using System.Net;
using System.Text.Json;
using EclipsVault.Core.Application.Networks;
using EclipsVault.Infrastructure.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// Redis-backed blacklist shared by every node. A block is keyed by the canonical range for the
/// offending address (<see cref="NetworkRules.ToBlockRange"/> — the exact host by default, or the
/// /24 / /64 when range blocking is enabled). Because that mapping is a pure, globally-configured
/// function of the address, the per-request "is this IP blocked?" check is a single O(1) key lookup
/// on the same canonical range, with no need to scan every stored block. Blocks persist until an
/// administrator (or break-glass recovery) lifts them, surviving restarts.
/// </summary>
public sealed class RedisIpBlacklist : IIpBlacklist
{
    private sealed record BlockRecord(string Reason, DateTimeOffset BlockedAtUtc);

    private readonly IConnectionMultiplexer _redis;
    private readonly string _prefix;
    private readonly ILogger<RedisIpBlacklist> _logger;
    private readonly bool _blockSurroundingRange;

    public RedisIpBlacklist(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> options,
        IOptions<IntrusionResponseOptions> intrusionOptions,
        ILogger<RedisIpBlacklist> logger)
    {
        _redis = redis;
        _prefix = options.Value.InstanceName;
        _logger = logger;
        _blockSurroundingRange = intrusionOptions.Value.BlockSurroundingRange;
    }

    public async Task BlockAsync(string sourceIp, string reason, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(sourceIp, out var address))
        {
            _logger.LogWarning("Cannot blacklist unparseable source address {SourceIp}", sourceIp);
            return;
        }

        var network = NetworkRules.ToBlockRange(address, _blockSurroundingRange).ToString();
        var record = JsonSerializer.Serialize(new BlockRecord(reason, DateTimeOffset.UtcNow));

        // First-write-wins so re-tripping the same range keeps the original timestamp/reason.
        var added = await _redis.GetDatabase().StringSetAsync(Key(network), record, when: When.NotExists);
        if (added)
        {
            _logger.LogCritical("Source range {Network} blacklisted — {Reason}", network, reason);
        }
    }

    public async Task<bool> IsBlockedAsync(IPAddress address, CancellationToken ct = default)
    {
        var network = NetworkRules.ToBlockRange(address, _blockSurroundingRange).ToString();
        return await _redis.GetDatabase().KeyExistsAsync(Key(network));
    }

    public async Task<IReadOnlyList<BlockedRangeDto>> ListAsync(CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var pattern = $"{_prefix}:ipblock:*";
        var results = new List<BlockedRangeDto>();

        foreach (var key in ScanKeys(pattern))
        {
            var value = await db.StringGetAsync(key);
            if (!value.HasValue)
            {
                continue;
            }

            var network = ((string)key!)[$"{_prefix}:ipblock:".Length..];
            var record = JsonSerializer.Deserialize<BlockRecord>((string)value!);
            if (record is not null)
            {
                results.Add(new BlockedRangeDto(network, record.Reason, record.BlockedAtUtc));
            }
        }

        return results.OrderByDescending(d => d.BlockedAtUtc).ToList();
    }

    public async Task<ILiftedIpBlock?> UnblockAsync(string network, CancellationToken ct = default)
    {
        var canonical = network.Trim();
        var db = _redis.GetDatabase();
        var record = await db.StringGetDeleteAsync(Key(canonical));
        if (record.HasValue)
        {
            _logger.LogWarning("Blacklisted range {Network} was unblocked by an administrator", network);
            return new LiftedBlock(db, Key(canonical), canonical, record);
        }

        return null;
    }

    private sealed class LiftedBlock(IDatabase database, RedisKey key, string network, RedisValue record) : ILiftedIpBlock
    {
        public string Network => network;

        public async Task RestoreAsync(CancellationToken ct = default)
        {
            var restored = await database.StringSetAsync(key, record, when: When.NotExists);
            if (!restored && await database.StringGetAsync(key) != record)
            {
                throw new InvalidOperationException("The original block could not be restored.");
            }
        }
    }

    public async Task<bool> UnblockAddressAsync(IPAddress address, CancellationToken ct = default)
    {
        // An address is only ever blocked under its own canonical range, so lifting that
        // one key fully unblocks it (break-glass recovery).
        var network = NetworkRules.ToBlockRange(address, _blockSurroundingRange).ToString();
        var removed = await _redis.GetDatabase().KeyDeleteAsync(Key(network));
        if (removed)
        {
            _logger.LogWarning("Blacklisted range {Network} lifted via break-glass recovery from {SourceIp}", network, address);
        }

        return removed;
    }

    private RedisKey Key(string network) => $"{_prefix}:ipblock:{network}";

    /// <summary>Enumerates matching keys across the connected primary endpoints (admin-only, infrequent).</summary>
    private IEnumerable<RedisKey> ScanKeys(string pattern)
    {
        foreach (var endpoint in _redis.GetEndPoints())
        {
            var server = _redis.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica)
            {
                continue;
            }

            foreach (var key in server.Keys(pattern: pattern))
            {
                yield return key;
            }
        }
    }
}
