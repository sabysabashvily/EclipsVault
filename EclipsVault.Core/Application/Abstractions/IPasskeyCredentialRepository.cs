using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Persistence port for WebAuthn credentials. Owns the <see cref="PasskeyCredential"/> aggregate only.
/// Adding and removing a credential are staged: they are saved with the account change that needs
/// them, through <see cref="IAccountChangeCommitter"/>.
/// </summary>
public interface IPasskeyCredentialRepository
{
    void StageAddition(PasskeyCredential credential);

    /// <summary>Looks up a credential by its authenticator-issued id (unique). Tracked, so the sign counter can be updated.</summary>
    Task<PasskeyCredential?> FindByCredentialIdAsync(byte[] credentialId, CancellationToken ct);

    /// <summary>
    /// Reloads the credential's stored values. Call it while holding its account's lock, so nothing
    /// changes them between the reload and the decision made on them.
    /// </summary>
    /// <returns>False when the credential was removed.</returns>
    Task<bool> RefreshAsync(PasskeyCredential credential, CancellationToken ct);

    Task<IReadOnlyList<PasskeyCredential>> ListForUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Loads one of the user's own credentials — scoped by user id so a caller can only touch their own passkeys.</summary>
    Task<PasskeyCredential?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken ct);

    /// <summary>
    /// Stores <paramref name="signCount"/>, immediately and only if the stored counter is still the
    /// one <paramref name="credential"/> holds. Every counter write holds the account lock, so this
    /// guards against a writer that does not, such as an older node still serving during a rollout.
    /// </summary>
    /// <returns>True when this call advanced the counter; false when another write changed it first.</returns>
    Task<bool> TryAdvanceSignCountAsync(PasskeyCredential credential, long signCount, CancellationToken ct);

    void StageRemoval(PasskeyCredential credential);
}
