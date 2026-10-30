using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.AccessRequests;

public interface IAccessRequestRepository
{
    Task AddAsync(AccessRequest request, CancellationToken ct);

    Task<AccessRequest?> FindAsync(Guid id, CancellationToken ct);

    Task<bool> HasPendingAsync(Guid secretId, Guid requesterUserId, CancellationToken ct);

    Task<IReadOnlyList<AccessRequest>> ListByRequesterAsync(Guid requesterUserId, CancellationToken ct);

    Task<IReadOnlyList<AccessRequestForReview>> ListPendingForReviewAsync(CancellationToken ct);

    Task<AccessRequestForReview?> FindForReviewAsync(Guid id, CancellationToken ct);

    Task<bool> UpdateAsync(AccessRequest request, CancellationToken ct);
}
