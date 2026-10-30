using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Abstractions;

public interface IAccessRequestCommitter
{
    /// <summary>
    /// Commits the pending request's approval and optional new or replacement share with their audit
    /// rows. False means another decision won; a competing share is a SharingException.
    /// </summary>
    Task<bool> ApproveAsync(AccessRequest request, SecretGrant? share, SecretGrant? expiredShare, CancellationToken ct);
}
