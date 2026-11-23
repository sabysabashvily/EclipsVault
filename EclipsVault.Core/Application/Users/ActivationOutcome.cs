namespace EclipsVault.Core.Application.Users;

/// <summary>What completing an account activation did.</summary>
public enum ActivationOutcome
{
    Activated,

    /// <summary>The token is unknown, used, expired or replaced. One answer for all four, so none is told apart.</summary>
    InvitationInvalid,

    /// <summary>The authenticator code did not match the invitation's seed. The invitation stays usable.</summary>
    CodeInvalid,
}
