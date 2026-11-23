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
        return await _redis.GetDatabase().KeyExistsAsync([Key(network), GuardKey(network)]) > 0;
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

    public async Task<IRecoveryLift> LiftForRecoveryAsync(IPAddress address)
    {
        // A replica configured for the other block size may have keyed this address either way.
        var networks = new[] { false, true }
            .Select(surrounding => NetworkRules.ToBlockRange(address, surrounding).ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var db = _redis.GetDatabase();
        var lift = Guid.NewGuid().ToString("N");
        var removed = new List<(string Network, RedisValue Record)>();
        var recovery = new RecoveryLift(this, db, lift, networks, removed);
        // A removal whose reply never came may still have run, and then its record is lost.
        string? unanswered = null;
        try
        {
            // Every range is guarded before any block goes. The guard has no expiry, so only settling the
            // lift releases it, and no lift of a block touches it.
            foreach (var network in networks)
            {
                await db.SetAddAsync(GuardKey(network), lift);
            }

            foreach (var network in networks)
            {
                unanswered = network;
                var record = await db.StringGetDeleteAsync(Key(network));
                unanswered = null;
                if (record.HasValue)
                {
                    removed.Add((network, record));
                    _logger.LogWarning("Blacklisted range {Network} lifted via break-glass recovery from {SourceIp}", network, address);
                }
            }
        }
        catch (Exception failure)
        {
            // No row can authorize a lift that failed halfway, so what went comes back, and the guard stays.
            var restored = await recovery.RestoreRemovedAsync() && unanswered is null;
            _logger.LogCritical(
                "Break-glass lift failed ({Failure}) after removing {Removed}, possibly removing {Unanswered} with its record unknown (restored: {Restored}); the source stays refused by {Guard}",
                failure.GetType().Name, string.Join(", ", removed.Select(block => block.Network)), unanswered ?? "nothing else", restored, recovery.Guard);
            throw;
        }

        return recovery;
    }

    private sealed class RecoveryLift(
        RedisIpBlacklist owner, IDatabase database, string lift, IReadOnlyList<string> guarded, IReadOnlyList<(string Network, RedisValue Record)> removed)
        : IRecoveryLift
    {
        public bool Lifted => removed.Count > 0;

        public string Guard => $"member {lift} of the Redis sets {string.Join(", ", guarded.Select(network => (string)owner.GuardKey(network)!))}";

        public async Task ReleaseAsync()
        {
            foreach (var network in guarded)
            {
                await database.SetRemoveAsync(owner.GuardKey(network), lift);
            }
        }

        public async Task<bool> TryRestoreAsync()
        {
            if (!await RestoreRemovedAsync())
            {
                return false;
            }

            try
            {
                await ReleaseAsync();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Puts back each removed record, keeping a block raised meanwhile. Never throws.</summary>
        /// <returns>Whether every removed record is confirmed back as it was.</returns>
        public async Task<bool> RestoreRemovedAsync()
        {
            try
            {
                var confirmed = true;
                foreach (var (network, record) in removed)
                {
                    // A block raised meanwhile on the same range is kept, and then this one is not back as it was.
                    confirmed &= await database.StringSetAsync(owner.Key(network), record, when: When.NotExists)
                        || await database.StringGetAsync(owner.Key(network)) == record;
                }

                return confirmed;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private RedisKey Key(string network) => $"{_prefix}:ipblock:{network}";

    // Outside the ipblock: namespace, so neither listing nor any lift of a block can see or remove it.
    private RedisKey GuardKey(string network) => $"{_prefix}:ipguard:{network}";

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
