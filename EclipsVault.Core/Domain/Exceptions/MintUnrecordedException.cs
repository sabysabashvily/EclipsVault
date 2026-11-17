namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// A minted credential's lease could not be recorded, and removing the credential also failed, so a live
/// login the vault never recorded may remain on the target. Still an audit failure to the caller.
/// </summary>
public sealed class MintUnrecordedException(string roleName, string login, Exception recordFailure)
    : AuditWriteFailedException("The lease of a minted credential could not be recorded, and the credential could not be removed.", recordFailure)
{
    public string RoleName { get; } = roleName;

    public string Login { get; } = login;
}
