namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when a new secret cannot be saved because its name is taken. Names are unique across the
/// vault, decoys and other projects' secrets included. Nothing was saved.
/// </summary>
public sealed class SecretNameTakenException : DomainException
{
    public SecretNameTakenException() : base("A secret with that name already exists.")
    {
    }
}
