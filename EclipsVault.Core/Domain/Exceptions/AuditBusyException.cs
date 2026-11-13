namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when the audit trail is asked for a verification or an export while another of the same
/// kind is running. Nothing was read or recorded.
/// </summary>
public sealed class AuditBusyException : DomainException
{
    public AuditBusyException() : base("Another verification or export of the audit trail is running.")
    {
    }
}
