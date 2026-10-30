using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.AccessRequests;

/// <summary>Persistence port for the access-request aggregate.</summary>
public interface IAccessRequestRepository
{
    Task AddAsync(AccessRequest request, CancellationToken ct);

    Task<AccessRequest?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>True when the user already has an open (pending) request for this secret.</summary>
    Task<bool> HasPendingAsync(Guid secretId, Guid requesterUserId, CancellationToken ct);

    Task<IReadOnlyList<AccessRequest>> ListByRequesterAsync(Guid requesterUserId, CancellationToken ct);

    /// <summary>All pending requests (administrator queue).</summary>
    Task<IReadOnlyList<AccessRequest>> ListPendingAsync(CancellationToken ct);

    /// <summary>Pending requests for one project (project-member queue).</summary>
    Task<IReadOnlyList<AccessRequest>> ListPendingForProjectAsync(string projectKey, CancellationToken ct);

    Task<bool> UpdateAsync(AccessRequest request, CancellationToken ct);
}
