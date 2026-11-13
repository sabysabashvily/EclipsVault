namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when the audit trail cannot be persisted. The vault operates fail-closed:
/// if this is thrown, the surrounding operation has been aborted and no secret
/// material has been (or will be) released.
/// </summary>
public class AuditWriteFailedException : DomainException
{
    public AuditWriteFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// For a refusal with no underlying error to report — the audit writer is shutting down, say.
    /// Nothing went wrong; the row simply cannot be promised, which fails the caller just the same.
    /// </summary>
    public AuditWriteFailedException(string message) : base(message)
    {
    }
}
