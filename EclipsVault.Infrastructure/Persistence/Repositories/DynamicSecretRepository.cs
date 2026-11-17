using EclipsVault.Core.Application.DynamicSecrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class DynamicSecretRepository : IDynamicSecretRepository
{
    private readonly EclipsVaultDbContext _context;

    public DynamicSecretRepository(EclipsVaultDbContext context) => _context = context;

    public async Task<IReadOnlyList<DynamicSecretRole>> ListRolesAsync(CancellationToken ct)
        => await _context.DynamicSecretRoles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);

    public Task<DynamicSecretRole?> FindRoleAsync(Guid roleId, CancellationToken ct)
        => _context.DynamicSecretRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId, ct);

    public async Task AddLeaseAsync(DynamicSecretLease lease, CancellationToken ct)
    {
        _context.DynamicSecretLeases.Add(lease);
        await SaveLeaseChangeAsync(ct);
    }

    public Task<DynamicSecretLease?> FindLeaseAsync(Guid leaseId, CancellationToken ct)
        => _context.DynamicSecretLeases.FirstOrDefaultAsync(l => l.Id == leaseId, ct);

    public async Task<IReadOnlyList<DynamicSecretLease>> ListLeasesForUserAsync(Guid userId, int max, CancellationToken ct)
        => await _context.DynamicSecretLeases
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.IssuedAtUtc)
            .Take(max)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DynamicSecretLease>> ListAllLeasesAsync(int max, CancellationToken ct)
        => await _context.DynamicSecretLeases
            .AsNoTracking()
            .OrderByDescending(l => l.IssuedAtUtc)
            .Take(max)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DynamicSecretLease>> ListDueLeasesAsync(DateTimeOffset asOfUtc, CancellationToken ct)
        => await _context.DynamicSecretLeases
            .Where(l => l.Status == LeaseStatus.Active && l.ExpiresAtUtc <= asOfUtc)
            .OrderBy(l => l.ExpiresAtUtc)
            .ToListAsync(ct);

    public async Task UpdateLeaseAsync(DynamicSecretLease lease, CancellationToken ct)
    {
        _context.DynamicSecretLeases.Update(lease);
        await SaveLeaseChangeAsync(ct);
    }

    private async Task SaveLeaseChangeAsync(CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The interceptor writes the lease's audit row in this transaction, so a failed save is an
            // audit failure to the caller, answered fail-closed like a standalone audit write.
            throw new AuditWriteFailedException("The lease and its audit row could not be stored.", ex);
        }
    }
}
