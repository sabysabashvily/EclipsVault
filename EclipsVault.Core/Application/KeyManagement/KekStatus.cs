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
