using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.ServiceAccounts;

/// <summary>
/// Reads service accounts and their keys. Writes are staged here and saved by
/// <see cref="Abstractions.IServiceAccountChanges"/> with their audit event; an edit to a found
/// account or key is staged by changing it.
/// </summary>
public interface IServiceAccountRepository
{
    Task<IReadOnlyList<ServiceAccount>> ListAsync(CancellationToken ct);

    Task<ServiceAccount?> FindAsync(Guid id, CancellationToken ct);

    Task<bool> ExistsByNameAsync(string name, CancellationToken ct);

    void StageAddition(ServiceAccount account);

    /// <summary>Stages removing the account; its keys go with it.</summary>
    void StageRemoval(ServiceAccount account);

    void StageNewKey(ApiKey key);

    /// <summary>Finds one of the service account's keys; another account's key is not found.</summary>
    Task<ApiKey?> FindKeyAsync(Guid serviceAccountId, Guid keyId, CancellationToken ct);

    /// <summary>Looks up a key by its stored hash, including its parent account, for authentication.</summary>
    Task<ApiKey?> FindKeyByHashAsync(string keyHash, CancellationToken ct);

    /// <summary>
    /// Saves when the key was last used, and nothing else about it, so a revoke or other change committed
    /// after the key was read is kept.
    /// </summary>
    Task RecordUseAsync(Guid keyId, DateTimeOffset usedAtUtc, CancellationToken ct);

    Task<IReadOnlyList<ApiKey>> ListKeysAsync(Guid serviceAccountId, CancellationToken ct);

    Task<int> CountActiveKeysAsync(Guid serviceAccountId, DateTimeOffset asOfUtc, CancellationToken ct);
}
