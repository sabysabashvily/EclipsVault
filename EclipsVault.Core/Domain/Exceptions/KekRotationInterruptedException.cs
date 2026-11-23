namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when a master key rotation pass stops for a reason other than auditing, such as a database
/// or key service failure, after at least one batch committed. The open batch rolled back; the counts
/// are what the committed batches moved.
/// </summary>
public sealed class KekRotationInterruptedException : Exception
{
    public KekRotationInterruptedException(int secretsRewrapped, int versionsRewrapped, Exception cause)
        : base("The master key rotation stopped partway because of an error.", cause)
    {
        SecretsRewrapped = secretsRewrapped;
        VersionsRewrapped = versionsRewrapped;
    }

    public int SecretsRewrapped { get; }

    public int VersionsRewrapped { get; }
}
