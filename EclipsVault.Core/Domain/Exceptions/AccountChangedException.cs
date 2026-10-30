namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when a change to a vault account is refused because another change to the same account
/// was saved after this one read it. Nothing was saved, so trying again starts from current state.
/// </summary>
public sealed class AccountChangedException : DomainException
{
    public AccountChangedException() : base("Your account was changed by another request. Reload the page and try again.")
    {
    }
}
