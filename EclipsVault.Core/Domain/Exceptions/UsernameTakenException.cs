namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when a new account cannot be saved because another account took its username first.
/// Nothing was saved.
/// </summary>
public sealed class UsernameTakenException : DomainException
{
    public UsernameTakenException(string? username = null) : base(username is null ? "That username is already taken." : $"The username '{username}' is already taken.")
    {
    }
}
