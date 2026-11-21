namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>The recipient already holds a live share on this secret, so no second one was made. The message is safe to show.</summary>
public sealed class ShareExistsException : DomainException
{
    public ShareExistsException(string message) : base(message)
    {
    }
}
