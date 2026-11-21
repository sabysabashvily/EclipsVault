using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Persistence boundary for dynamic-secret roles and their leases. Lease inserts and status
/// changes are audited atomically with the change by the SaveChanges interceptor, so this port
/// carries no audit concern.
/// </summary>
public interface IDynamicSecretRepository
{
    Task<IReadOnlyList<DynamicSecretRole>> ListRolesAsync(CancellationToken ct);

    Task<DynamicSecretRole?> FindRoleAsync(Guid roleId, CancellationToken ct);

    Task AddLeaseAsync(DynamicSecretLease lease, CancellationToken ct);

    Task<DynamicSecretLease?> FindLeaseAsync(Guid leaseId, CancellationToken ct);

    /// <summary>Every lease for one user, newest first — their "checked-out credentials" view.</summary>
    Task<IReadOnlyList<DynamicSecretLease>> ListLeasesForUserAsync(Guid userId, int max, CancellationToken ct);

    /// <summary>Every lease across all users, newest first — the admin view.</summary>
    Task<IReadOnlyList<DynamicSecretLease>> ListAllLeasesAsync(int max, CancellationToken ct);

    /// <summary>Active leases whose TTL has elapsed — the reaper's work list.</summary>
    Task<IReadOnlyList<DynamicSecretLease>> ListDueLeasesAsync(DateTimeOffset asOfUtc, CancellationToken ct);

    Task UpdateLeaseAsync(DynamicSecretLease lease, CancellationToken ct);
}
