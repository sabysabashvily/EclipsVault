namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when a sign-in or credential change was begun under an earlier credential generation than
/// the account now has. Nothing was saved; starting again proves the account's current credentials.
/// </summary>
public sealed class StaleProofException : DomainException
{
    public StaleProofException() : base("Your account's sign-in details changed while this was in progress. Start again.")
    {
    }
}
