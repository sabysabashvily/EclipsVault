namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when a value an administrator entered for an account breaks the account's input rules.
/// <see cref="Field"/> names the request property that holds the value. Nothing was changed.
/// </summary>
public sealed class AccountInputException : VaultAdminException
{
    public AccountInputException(string field, string message) : base(message)
    {
        Field = field;
    }

    public string Field { get; }
}
