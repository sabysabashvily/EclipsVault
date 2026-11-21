namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when an administrative action that only applies to activated accounts (such as resetting
/// an authenticator) targets an account still awaiting activation. Nothing was changed.
/// </summary>
public sealed class AccountNotActivatedException : VaultAdminException
{
    public AccountNotActivatedException(string message) : base(message)
    {
    }
}
