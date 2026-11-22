namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Another change to the secret committed after this one read it, so this one wrote nothing rather than
/// overwrite it. The message is safe to show.
/// </summary>
public sealed class SecretChangedException : DomainException
{
    public SecretChangedException()
        : base("This secret changed while you were working on it. Reload and try again.")
    {
    }
}
