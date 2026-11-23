namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when an invitation cannot be sent because real SMTP delivery or <c>Identity:PublicUrl</c>
/// is not configured. Nothing was changed.
/// </summary>
public sealed class InvitationsUnavailableException : VaultAdminException
{
    public InvitationsUnavailableException(string message) : base(message)
    {
    }
}
