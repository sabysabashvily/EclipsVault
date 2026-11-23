namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when an invitation action (resending or replacing the invitation address) targets an
/// account that has already been activated. Nothing was changed.
/// </summary>
public sealed class AccountAlreadyActiveException : VaultAdminException
{
    public AccountAlreadyActiveException(string message) : base(message)
    {
    }
}
