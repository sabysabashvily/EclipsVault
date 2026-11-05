using System.Net;

namespace EclipsVault.Core.Application.Abstractions;

public sealed record BlockedRangeDto(string Network, string Reason, DateTimeOffset BlockedAtUtc);

/// <summary>
/// Source-address blacklist consulted by middleware on every request. Blocking an address blocks
/// the exact host by default, or its surrounding range (/24 for IPv4, /64 for IPv6) when range
/// blocking is enabled — see <c>IntrusionResponseOptions.BlockSurroundingRange</c>. Administrators
/// can inspect and lift blocks at runtime. Backed by a shared store (Redis) in multi-node
/// deployments so a block raised on one node is enforced by every node.
/// </summary>
public interface IIpBlacklist
{
    Task BlockAsync(string sourceIp, string reason, CancellationToken ct = default);

    Task<bool> IsBlockedAsync(IPAddress address, CancellationToken ct = default);

    Task<IReadOnlyList<BlockedRangeDto>> ListAsync(CancellationToken ct = default);

    Task<ILiftedIpBlock?> UnblockAsync(string network, CancellationToken ct = default);

    /// <summary>Removes every blocked range containing the address (break-glass recovery).</summary>
    Task<bool> UnblockAddressAsync(IPAddress address, CancellationToken ct = default);
}
