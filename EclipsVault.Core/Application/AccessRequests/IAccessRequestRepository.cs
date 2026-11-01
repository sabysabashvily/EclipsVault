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

    /// <summary>
    /// Saves a reject or withdraw decision with its audit row, only while the stored request is still
    /// pending. False when another decision committed first.
    /// </summary>
    Task<bool> SaveDecisionAsync(AccessRequest request, CancellationToken ct);
}
