namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// A managed secret's value is the live password of a backend login, so restoring a version would change
/// only the vault's copy. Nothing is changed. The message is safe to show.
/// </summary>
public sealed class RestoreManagedException : DomainException
{
    public RestoreManagedException(string principal)
        : base($"This secret is the live password of '{principal}'. Rotate it upstream instead; earlier versions stay readable.")
    {
    }
}
