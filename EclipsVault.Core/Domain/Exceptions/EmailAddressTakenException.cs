namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when an account cannot take an email address because another account signs in with it.
/// The message never says which account, so it is safe to show to whoever asked.
/// </summary>
public sealed class EmailAddressTakenException : DomainException
{
    public EmailAddressTakenException() : base("That email address is already used by another account.")
    {
    }
}
