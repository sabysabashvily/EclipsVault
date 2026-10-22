using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Secrets;

/// <summary>Persistence boundary for <see cref="SecretGrant"/> access grants.</summary>
public interface ISecretGrantRepository
{
    Task AddAsync(SecretGrant grant, CancellationToken ct);

    Task<SecretGrant?> FindAsync(Guid grantId, CancellationToken ct);

    Task<bool> RemoveAsync(Guid grantId, CancellationToken ct);

    /// <summary>True when an unexpired grant exists for this user on this secret.</summary>
    Task<bool> HasActiveGrantAsync(Guid userId, Guid secretId, DateTimeOffset asOfUtc, CancellationToken ct);

    /// <summary>True when any grant (active or not) already links this user and secret.</summary>
    Task<bool> ExistsAsync(Guid userId, Guid secretId, CancellationToken ct);

    Task<IReadOnlyList<SecretGrant>> ListForSecretAsync(Guid secretId, CancellationToken ct);

    /// <summary>Active grants for a user, joined to their still-accessible (non-shredded, non-expired) secrets.</summary>
    Task<IReadOnlyList<SharedSecretDto>> ListSharedWithUserAsync(Guid userId, DateTimeOffset asOfUtc, CancellationToken ct);

    /// <summary>Active shares issued by one account, joined to the (non-shredded) secrets they cover.</summary>
    Task<IReadOnlyList<OutgoingShareDto>> ListIssuedByAsync(Guid issuerUserId, DateTimeOffset asOfUtc, CancellationToken ct);
}
