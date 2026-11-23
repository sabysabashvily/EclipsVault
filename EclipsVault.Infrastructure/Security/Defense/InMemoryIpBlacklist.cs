using System.Collections.Concurrent;
using System.Net;
using EclipsVault.Core.Application.Networks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// Process-local blacklist of offending hosts. By default a block covers the exact host; it widens
/// to the surrounding /24 (IPv4) or /64 (IPv6) only when <see cref="IntrusionResponseOptions.BlockSurroundingRange"/>
/// is set. Loopback is always pinned to its exact address. Administrators can inspect and lift
/// blocks from the Networks console. Multi-node deployments use the Redis-backed implementation so
/// a block raised on one node is enforced by every node.
/// </summary>
public sealed class InMemoryIpBlacklist : IIpBlacklist
{
    private sealed record Entry(IPNetwork Network, BlockedRangeDto Dto);

    private readonly ConcurrentDictionary<string, Entry> _blocked = new();
    // Break-glass guards, keyed by the lift that holds them and the range they cover.
    private readonly ConcurrentDictionary<(Guid Lift, string Network), IPNetwork> _guards = new();
    // Checks, recovery lifts and their settlement take turns, so a check never reads a block already
    // lifted and then its guard already released, which would let one request through unrecorded.
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private readonly ILogger<InMemoryIpBlacklist> _logger;
    private readonly IOptions<IntrusionResponseOptions> _options;

    public InMemoryIpBlacklist(TimeProvider clock, IOptions<IntrusionResponseOptions> options, ILogger<InMemoryIpBlacklist> logger)
    {
        _clock = clock;
        _logger = logger;
        _options = options;
    }

    public Task BlockAsync(string sourceIp, string reason, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(sourceIp, out var address))
        {
            _logger.LogWarning("Cannot blacklist unparseable source address {SourceIp}", sourceIp);
            return Task.CompletedTask;
        }

        var network = NetworkRules.ToBlockRange(address, _options.Value.BlockSurroundingRange);
        var key = network.ToString();
        var entry = new Entry(network, new BlockedRangeDto(key, reason, _clock.GetUtcNow()));

        if (_blocked.TryAdd(key, entry))
        {
            _logger.LogCritical("Source range {Network} blacklisted — {Reason}", key, reason);
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsBlockedAsync(IPAddress address, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_blocked.Values.Any(entry => NetworkRules.Contains(entry.Network, address))
                || _guards.Values.Any(network => NetworkRules.Contains(network, address)));
        }
    }

    public Task<IReadOnlyList<BlockedRangeDto>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BlockedRangeDto>>(
            _blocked.Values.Select(e => e.Dto).OrderByDescending(d => d.BlockedAtUtc).ToList());

    public Task<ILiftedIpBlock?> UnblockAsync(string network, CancellationToken ct = default)
    {
        if (_blocked.TryRemove(network.Trim(), out var entry))
        {
            _logger.LogWarning("Blacklisted range {Network} was unblocked by an administrator", network);
            return Task.FromResult<ILiftedIpBlock?>(new LiftedBlock(_blocked, entry));
        }

        return Task.FromResult<ILiftedIpBlock?>(null);
    }

    private sealed class LiftedBlock(ConcurrentDictionary<string, Entry> blocked, Entry entry) : ILiftedIpBlock
    {
        public string Network => entry.Dto.Network;

        public Task RestoreAsync(CancellationToken ct = default)
        {
            if (blocked.GetOrAdd(Network, entry) != entry)
            {
                throw new InvalidOperationException("The original block could not be restored.");
            }

            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<string>> ListGuardsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                _guards.Keys.Select(guard => guard.Network).Distinct().Order(StringComparer.Ordinal).ToList());
        }
    }

    public Task<ILiftedIpBlock?> ReleaseGuardAsync(string network, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var released = _guards.Where(guard => guard.Key.Network == network.Trim()).ToList();
            foreach (var guard in released)
            {
                _guards.TryRemove(guard);
            }

            if (released.Count == 0)
            {
                return Task.FromResult<ILiftedIpBlock?>(null);
            }

            _logger.LogWarning("Break-glass guard on {Network} was released by an administrator", network);
            return Task.FromResult<ILiftedIpBlock?>(new ReleasedGuard(this, network.Trim(), released));
        }
    }

    private sealed class ReleasedGuard(InMemoryIpBlacklist owner, string network, IReadOnlyList<KeyValuePair<(Guid Lift, string Network), IPNetwork>> released)
        : ILiftedIpBlock
    {
        public string Network => network;

        public Task RestoreAsync(CancellationToken ct = default)
        {
            lock (owner._gate)
            {
                foreach (var guard in released)
                {
                    owner._guards[guard.Key] = guard.Value;
                }
            }

            return Task.CompletedTask;
        }
    }

    public Task<IRecoveryLift> LiftForRecoveryAsync(IPAddress address)
    {
        lock (_gate)
        {
            return Task.FromResult<IRecoveryLift>(Lift(address));
        }
    }

    private RecoveryLift Lift(IPAddress address)
    {
        var lift = Guid.NewGuid();
        var canonical = NetworkRules.ToBlockRange(address, _options.Value.BlockSurroundingRange);
        _guards[(lift, canonical.ToString())] = canonical;
        var removed = new List<Entry>();
        foreach (var (key, entry) in _blocked)
        {
            if (!NetworkRules.Contains(entry.Network, address))
            {
                continue;
            }

            // Guarded before it goes, so no address the range covered is let in before the lift is settled.
            _guards[(lift, key)] = entry.Network;
            if (_blocked.TryRemove(new KeyValuePair<string, Entry>(key, entry)))
            {
                removed.Add(entry);
                _logger.LogWarning("Blacklisted range {Network} lifted via break-glass recovery from {SourceIp}", key, address);
            }
        }

        return new RecoveryLift(this, lift, removed);
    }

    private sealed class RecoveryLift(InMemoryIpBlacklist owner, Guid lift, IReadOnlyList<Entry> removed) : IRecoveryLift
    {
        public bool Lifted => removed.Count > 0;

        public IReadOnlyList<string> Removed => removed.Select(entry => entry.Dto.Network).ToList();

        public string Guard
        {
            get
            {
                lock (owner._gate)
                {
                    var ranges = owner._guards.Keys.Where(guard => guard.Lift == lift).Select(guard => guard.Network);
                    return $"lift {lift} of the in-process blacklist, guarding {string.Join(", ", ranges)} until the process restarts";
                }
            }
        }

        public Task ReleaseAsync()
        {
            lock (owner._gate)
            {
                Release();
            }

            return Task.CompletedTask;
        }

        public Task<bool> TryRestoreAsync()
        {
            lock (owner._gate)
            {
                // A block raised meanwhile on the same range is kept, and then this one is not back as it was.
                var restored = removed.Count(entry => owner._blocked.GetOrAdd(entry.Dto.Network, entry) == entry);
                if (restored != removed.Count)
                {
                    return Task.FromResult(false);
                }

                Release();
                return Task.FromResult(true);
            }
        }

        private void Release()
        {
            foreach (var guard in owner._guards.Keys.Where(guard => guard.Lift == lift))
            {
                owner._guards.TryRemove(guard, out _);
            }
        }
    }
}
