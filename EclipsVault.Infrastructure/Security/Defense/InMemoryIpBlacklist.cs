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
    private readonly TimeProvider _clock;
    private readonly ILogger<InMemoryIpBlacklist> _logger;
    private readonly bool _blockSurroundingRange;

    public InMemoryIpBlacklist(TimeProvider clock, IOptions<IntrusionResponseOptions> options, ILogger<InMemoryIpBlacklist> logger)
    {
        _clock = clock;
        _logger = logger;
        _blockSurroundingRange = options.Value.BlockSurroundingRange;
    }

    public Task BlockAsync(string sourceIp, string reason, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(sourceIp, out var address))
        {
            _logger.LogWarning("Cannot blacklist unparseable source address {SourceIp}", sourceIp);
            return Task.CompletedTask;
        }

        var network = NetworkRules.ToBlockRange(address, _blockSurroundingRange);
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
        foreach (var entry in _blocked.Values)
        {
            if (NetworkRules.Contains(entry.Network, address))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
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

    public Task<bool> UnblockAddressAsync(IPAddress address, CancellationToken ct = default)
    {
        var removed = false;
        foreach (var (key, entry) in _blocked)
        {
            if (NetworkRules.Contains(entry.Network, address) && _blocked.TryRemove(key, out _))
            {
                removed = true;
                _logger.LogWarning("Blacklisted range {Network} lifted via break-glass recovery from {SourceIp}", key, address);
            }
        }

        return Task.FromResult(removed);
    }
}
