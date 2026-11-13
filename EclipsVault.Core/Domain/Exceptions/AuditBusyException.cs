namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>Raised when the audit trail is asked for a verification while another is running. Nothing was read or recorded.</summary>
public sealed class AuditBusyException : DomainException
{
    public AuditBusyException() : base("Another verification of the audit trail is running.")
    {
    }
}
