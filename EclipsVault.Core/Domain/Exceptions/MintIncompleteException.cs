namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Minting failed part-way and removing the login also failed, so a login the vault never recorded may
/// remain on the target. Carries the login's name, never its password, for the operator's alert.
/// </summary>
public sealed class MintIncompleteException(string roleName, string login) : DomainException(FailureMessage)
{
    public const string FailureMessage =
        "The backend failed part-way through, and the vault could not confirm that the login it started to create was removed. " +
        "No password was shown or kept. An administrator must check the target.";

    public string RoleName { get; } = roleName;

    public string Login { get; } = login;
}
