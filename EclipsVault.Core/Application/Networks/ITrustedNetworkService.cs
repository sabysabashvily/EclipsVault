using System.Net;

namespace EclipsVault.Core.Application.Networks;

public sealed record TrustedNetworkDto(Guid Id, string Cidr, string Label, string AddedBy, DateTimeOffset CreatedAtUtc);

/// <summary>
/// Runtime-managed trusted networks for the ABAC network rule. Lookups are cached
/// briefly and the cache is evicted on every mutation, so a newly trusted address
/// takes effect immediately. All mutations are audited.
/// </summary>
public interface ITrustedNetworkService
{
    Task<bool> IsTrustedAsync(IPAddress address, CancellationToken ct);

    Task<IReadOnlyList<TrustedNetworkDto>> ListAsync(CancellationToken ct);

    /// <summary>Accepts a bare IP or CIDR; normalizes and persists it. Throws NetworkInputException on invalid input and NetworkExistsException when the range is already trusted.</summary>
    Task<TrustedNetworkDto> AddAsync(string cidrOrIp, string label, CancellationToken ct);

    Task<bool> RemoveAsync(Guid id, CancellationToken ct);
}
