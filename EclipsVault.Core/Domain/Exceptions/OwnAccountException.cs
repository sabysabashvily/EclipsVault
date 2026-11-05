namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when an administrator resets, disables or deletes the account they are signed in with.
/// Profile governs those changes to your own account. Nothing was changed.
/// </summary>
public sealed class OwnAccountException : VaultAdminException
{
    public OwnAccountException(string message) : base(message)
    {
    }
}
