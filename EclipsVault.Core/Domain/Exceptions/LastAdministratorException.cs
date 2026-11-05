namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when demoting, disabling or deleting an account would leave the vault with no enabled
/// administrator. Nothing was changed.
/// </summary>
public sealed class LastAdministratorException : VaultAdminException
{
    public LastAdministratorException(string message) : base(message)
    {
    }
}
