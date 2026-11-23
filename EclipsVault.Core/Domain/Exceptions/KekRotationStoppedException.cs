namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when a master key rotation batch cannot be recorded in the audit trail. That batch was
/// rolled back and the pass stopped; the counts are what the batches committed before it moved.
/// It is an audit failure, so a caller that does not report the counts still fails closed.
/// </summary>
public sealed class KekRotationStoppedException : AuditWriteFailedException
{
    public KekRotationStoppedException(int secretsRewrapped, int versionsRewrapped, AuditWriteFailedException unrecorded)
        : base("The master key rotation stopped because a batch could not be recorded in the audit trail.", unrecorded)
    {
        SecretsRewrapped = secretsRewrapped;
        VersionsRewrapped = versionsRewrapped;
    }

    public int SecretsRewrapped { get; }

    public int VersionsRewrapped { get; }
}
