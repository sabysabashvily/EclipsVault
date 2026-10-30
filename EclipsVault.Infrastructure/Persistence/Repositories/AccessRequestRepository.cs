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

    public async Task<IReadOnlyList<AccessRequestForReview>> ListPendingForReviewAsync(CancellationToken ct)
        => (await ForReview(_context.AccessRequests.Where(r => r.Status == AccessRequestStatus.Pending)).ToListAsync(ct))
            .OrderByDescending(r => r.Request.CreatedAtUtc)
            .ToList();

    public Task<AccessRequestForReview?> FindForReviewAsync(Guid id, CancellationToken ct)
        => ForReview(_context.AccessRequests.Where(r => r.Id == id)).SingleOrDefaultAsync(ct);

    private IQueryable<AccessRequestForReview> ForReview(IQueryable<AccessRequest> requests)
        => from request in requests.AsNoTracking()
           join secret in _context.Secrets.AsNoTracking() on request.SecretId equals secret.Id
           select new AccessRequestForReview(
               AccessRequestDto.From(request),
               new SecretSummaryDto(secret.Id, secret.Name, secret.ProjectKey, secret.Environment, secret.Sensitivity,
                   secret.CreatedAtUtc, secret.ExpiresAtUtc));

    public Task<bool> UpdateAsync(AccessRequest request, CancellationToken ct)
        => new AccessRequestCommitter(_context).DecideAsync(request, ct);
}
