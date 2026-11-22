namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Another writer holds the lock on the backend login this managed secret is bound to, so this change
/// was refused before touching the backend. The message is safe to show.
/// </summary>
public sealed class ChangeInProgressException : DomainException
{
    public ChangeInProgressException()
        : base("Another change to this secret is in progress. Try again in a moment.")
    {
    }
}
