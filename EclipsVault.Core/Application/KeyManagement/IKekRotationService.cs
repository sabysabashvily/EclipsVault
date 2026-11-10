using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.KeyManagement;

/// <summary>How many stored items are wrapped under a given KEK.</summary>
public sealed record KekUsage(string KekId, bool IsCurrent, bool IsKnown, int SecretCount, int VersionCount)
{
    public int Total => SecretCount + VersionCount;
}

/// <summary>
/// Snapshot of the key-encryption-key estate for the admin Encryption page.
/// <paramref name="AwaitingRotation"/> counts the secrets and versions a pass can move;
/// <paramref name="OnMissingKeys"/> counts those left under master keys that are not loaded.
/// </summary>
public sealed record KekStatus(
    string CurrentKekId,
    IReadOnlyList<string> KnownKekIds,
    IReadOnlyList<KekUsage> Usage,
    int AwaitingRotation,
    int OnMissingKeys);

/// <summary>
/// Outcome of a rotation pass. <paramref name="OnMissingKeys"/> counts the secrets and versions left
/// under master keys that are not loaded.
/// </summary>
public sealed record KekRotationResult(string CurrentKekId, int SecretsRewrapped, int VersionsRewrapped, int OnMissingKeys);

/// <summary>
/// Key lifecycle management. Rotation re-wraps every secret's (and archived version's) data-encryption
/// key under the <em>current</em> master KEK and retires the old one — the payload ciphertext never
/// changes. The operator supplies the new current KEK and keeps the previous key available as a retired
/// key (so existing DEKs can still be unwrapped) until rotation completes; afterwards the old key can be
/// removed. Aligns with NIST 800-57 cryptoperiods and PCI-DSS 3.6/3.7.
/// </summary>
public interface IKekRotationService
{
    Task<KekStatus> GetStatusAsync(CancellationToken ct);

    /// <summary>
    /// Runs one pass in batches (ADR-0029). Throws <see cref="KekRotationRunningException"/>, having changed
    /// nothing, while another pass runs, and <see cref="KekRotationStoppedException"/> when a batch cannot be
    /// recorded; the batches committed before it stay, and the exception counts what they moved.
    /// </summary>
    Task<KekRotationResult> RotateAsync(CancellationToken ct);
}
