using EclipsVault.Core.Application.DynamicSecrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Tests.TestDoubles;

/// <summary>
/// One role and its leases in memory. A save it is told to fail rewinds the lease and throws the audit
/// failure, as the vault's database context does when a change and its audit row cannot commit.
/// </summary>
public sealed class FakeLeaseRepository(DynamicSecretRole role) : IDynamicSecretRepository
{
    private readonly Dictionary<Guid, DynamicSecretLease> _saved = [];

    public bool RoleRemoved { get; set; }

    public bool AddFails { get; init; }

    public int UpdatesToFail { get; set; }

    public List<DynamicSecretLease> Leases { get; } = [];

    public Task<IReadOnlyList<DynamicSecretRole>> ListRolesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DynamicSecretRole>>(RoleRemoved ? [] : [role]);

    public Task<DynamicSecretRole?> FindRoleAsync(Guid roleId, CancellationToken ct)
        => Task.FromResult(RoleRemoved || roleId != role.Id ? null : role);

    public Task AddLeaseAsync(DynamicSecretLease lease, CancellationToken ct)
    {
        if (AddFails) throw new AuditWriteFailedException("The lease and its audit row could not be stored.");
        Leases.Add(lease);
        _saved[lease.Id] = Copy(lease);
        return Task.CompletedTask;
    }

    public Task<DynamicSecretLease?> FindLeaseAsync(Guid leaseId, CancellationToken ct)
        => Task.FromResult(Leases.FirstOrDefault(l => l.Id == leaseId));

    public Task<IReadOnlyList<DynamicSecretLease>> ListLeasesForUserAsync(Guid userId, int max, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DynamicSecretLease>>(Leases.Where(l => l.UserId == userId).ToList());

    public Task<IReadOnlyList<DynamicSecretLease>> ListAllLeasesAsync(int max, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DynamicSecretLease>>(Leases);

    public Task<IReadOnlyList<DynamicSecretLease>> ListDueLeasesAsync(DateTimeOffset asOfUtc, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DynamicSecretLease>>(Leases.Where(l => l.IsDue(asOfUtc)).ToList());

    public Task UpdateLeaseAsync(DynamicSecretLease lease, CancellationToken ct)
    {
        if (UpdatesToFail > 0)
        {
            UpdatesToFail--;
            var saved = _saved[lease.Id];
            lease.Status = saved.Status;
            lease.ClosedAtUtc = saved.ClosedAtUtc;
            lease.RevocationError = saved.RevocationError;
            throw new AuditWriteFailedException("The lease and its audit row could not be stored.");
        }

        _saved[lease.Id] = Copy(lease);
        return Task.CompletedTask;
    }

    private static DynamicSecretLease Copy(DynamicSecretLease lease) => new()
    {
        Status = lease.Status,
        ClosedAtUtc = lease.ClosedAtUtc,
        RevocationError = lease.RevocationError,
    };
}
