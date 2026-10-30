using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class AccessRequestRepository : IAccessRequestRepository
{
    private readonly EclipsVaultDbContext _context;

    public AccessRequestRepository(EclipsVaultDbContext context) => _context = context;

    public async Task AddAsync(AccessRequest request, CancellationToken ct)
    {
        _context.AccessRequests.Add(request);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AuditWriteFailedException("The access request and its audit row could not be persisted.", ex);
        }
    }

    public Task<AccessRequest?> FindAsync(Guid id, CancellationToken ct)
        => _context.AccessRequests.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<bool> HasPendingAsync(Guid secretId, Guid requesterUserId, CancellationToken ct)
        => _context.AccessRequests.AnyAsync(
            r => r.SecretId == secretId && r.RequesterUserId == requesterUserId && r.Status == AccessRequestStatus.Pending, ct);

    public async Task<IReadOnlyList<AccessRequest>> ListByRequesterAsync(Guid requesterUserId, CancellationToken ct)
        => await _context.AccessRequests.AsNoTracking()
            .Where(r => r.RequesterUserId == requesterUserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AccessRequest>> ListPendingAsync(CancellationToken ct)
        => await _context.AccessRequests.AsNoTracking()
            .Where(r => r.Status == AccessRequestStatus.Pending)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AccessRequest>> ListPendingForProjectAsync(string projectKey, CancellationToken ct)
        => await _context.AccessRequests.AsNoTracking()
            .Where(r => r.Status == AccessRequestStatus.Pending && r.ProjectKey == projectKey)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<bool> UpdateAsync(AccessRequest request, CancellationToken ct)
        => new AccessRequestCommitter(_context).DecideAsync(request, ct);
}
