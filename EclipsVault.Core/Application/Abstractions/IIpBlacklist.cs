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

    /// <summary>
    /// Break-glass recovery's lift: guards the address's range first, so the address stays refused
    /// until the lift is settled, then removes every blocked range containing the address and keeps
    /// exactly what it removed. Not cancellable: a removal must always be settled.
    /// </summary>
    Task<IRecoveryLift> LiftForRecoveryAsync(IPAddress address);

    /// <summary>The ranges held by a guard whose lift was marked stuck; a lift still in flight is not listed.</summary>
    Task<IReadOnlyList<string>> ListStuckGuardsAsync(CancellationToken ct = default);

    /// <summary>
    /// Claims the stuck guards on <paramref name="network"/> for an administrator's release, leaving them
    /// in force. Null when there is none to claim; guards of lifts still in flight are never claimed.
    /// </summary>
    Task<IClaimedGuard?> ClaimStuckGuardAsync(string network, CancellationToken ct = default);
}
