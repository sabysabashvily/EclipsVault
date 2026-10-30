using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.AccessRequests;

public interface IAccessRequestRepository
{
    Task AddAsync(AccessRequest request, CancellationToken ct);

    Task<AccessRequest?> FindAsync(Guid id, CancellationToken ct);

    Task<bool> HasPendingAsync(Guid secretId, Guid requesterUserId, CancellationToken ct);

    Task<IReadOnlyList<AccessRequest>> ListByRequesterAsync(Guid requesterUserId, CancellationToken ct);

    /// <summary>All pending requests (administrator queue).</summary>
    Task<IReadOnlyList<AccessRequest>> ListPendingAsync(CancellationToken ct);

    /// <summary>Pending requests for one project (project-member queue).</summary>
    Task<IReadOnlyList<AccessRequest>> ListPendingForProjectAsync(string projectKey, CancellationToken ct);

    Task<bool> UpdateAsync(AccessRequest request, CancellationToken ct);
}
