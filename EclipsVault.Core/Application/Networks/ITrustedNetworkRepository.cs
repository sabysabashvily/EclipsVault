using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Networks;

/// <summary>
/// Persistence boundary for runtime-managed trusted networks. Adds and removes are audited
/// atomically with the change by the SaveChanges interceptor, so this port carries no audit
/// concern of its own.
/// </summary>
public interface ITrustedNetworkRepository
{
    /// <summary>
    /// The trusted ranges in CIDR form. Consulted on every ABAC evaluation, so implementations
    /// are expected to cache it and evict on mutation.
    /// </summary>
    Task<IReadOnlyList<string>> ListCidrsAsync(CancellationToken ct);

    Task<IReadOnlyList<TrustedNetwork>> ListAsync(CancellationToken ct);

    Task<bool> ExistsAsync(string cidr, CancellationToken ct);

    /// <exception cref="NetworkExistsException">Another request trusted the range first; nothing was saved.</exception>
    /// <exception cref="AuditWriteFailedException">The network and its audit row could not be stored; nothing was saved.</exception>
    Task AddAsync(TrustedNetwork network, CancellationToken ct);

    Task<TrustedNetwork?> FindAsync(Guid id, CancellationToken ct);

    /// <exception cref="AuditWriteFailedException">The removal and its audit row could not be stored; nothing was removed.</exception>
    Task RemoveAsync(TrustedNetwork network, CancellationToken ct);
}
