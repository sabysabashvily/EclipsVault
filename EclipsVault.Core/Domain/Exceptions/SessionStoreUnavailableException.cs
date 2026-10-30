namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when an account operation could not read the session store inside its transaction, so it
/// could not check that no sign-out landed first. The transaction was rolled back: nothing was saved.
/// </summary>
public sealed class SessionStoreUnavailableException : DomainException
{
    public SessionStoreUnavailableException(Exception innerException)
        : base("The session store is unavailable, so nothing was saved.", innerException)
    {
    }
}
