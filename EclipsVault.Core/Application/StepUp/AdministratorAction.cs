namespace EclipsVault.Core.Application.StepUp;

public enum AdministratorAction
{
    CreateAccount,
    ResendInvitation,
    ReplaceInvitationAddress,
    ChangeAccountRole,
    EnableAccount,
    UnlockAccount,
    ResetAuthenticator,
    DisableAccount,
    DeleteAccount,
    SignOutAccount,
    IssueApiKey,
    EnableServiceAccount,
    CreateServiceAccount,
    DisableServiceAccount,
    RevokeApiKey,
    DeleteServiceAccount,
    TrustNetwork,
    LiftBlock,
    RemoveTrustedNetwork,
    ExportAuditTrail
}
