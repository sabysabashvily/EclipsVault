using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Secrets;

/// <summary>Persistence boundary for <see cref="SecretGrant"/> access grants.</summary>
public interface ISecretGrantRepository
{
    /// <summary>
    /// Saves a new share with its audit row. False when the save failed because a share for that
    /// recipient now exists; any other failed save throws <c>AuditWriteFailedException</c>.
    /// </summary>
    Task<bool> AddAsync(SecretGrant grant, CancellationToken ct);

    /// <summary>
    /// Deletes <paramref name="expired"/> and saves <paramref name="grant"/> in one transaction,
    /// audited as one share that replaced an expired one. Fails as <see cref="AddAsync"/> does.
    /// </summary>
    Task<bool> ReplaceExpiredAsync(SecretGrant expired, SecretGrant grant, CancellationToken ct);

    Task<SecretGrant?> FindAsync(Guid grantId, CancellationToken ct);

    Task<bool> RemoveAsync(Guid grantId, CancellationToken ct);

    /// <summary>True when an unexpired grant exists for this user on this secret.</summary>
    Task<bool> HasActiveGrantAsync(Guid userId, Guid secretId, DateTimeOffset asOfUtc, CancellationToken ct);

    /// <summary>The grant, active or expired, that links this user and secret, if there is one.</summary>
    Task<SecretGrant?> FindForRecipientAsync(Guid userId, Guid secretId, CancellationToken ct);

    Task<IReadOnlyList<SecretGrant>> ListForSecretAsync(Guid secretId, CancellationToken ct);

    /// <summary>
    /// Active grants for a user, joined to their still-accessible secrets: not shredded, not expired,
    /// and not decoys, which the Secrets list never enumerates either.
    /// </summary>
    Task<IReadOnlyList<SharedSecretDto>> ListSharedWithUserAsync(Guid userId, DateTimeOffset asOfUtc, CancellationToken ct);

    /// <summary>Active shares issued by one account, joined to the (non-shredded) secrets they cover.</summary>
    Task<IReadOnlyList<OutgoingShareDto>> ListIssuedByAsync(Guid issuerUserId, DateTimeOffset asOfUtc, CancellationToken ct);
}
