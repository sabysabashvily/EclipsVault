namespace EclipsVault.Web.Models.Ui;

public sealed record ProfileChangesResponse(
    ProfileActionResponse ChangePassword,
    ProfileActionResponse ResetAuthenticator,
    ProfileActionResponse ReplaceRecoveryCodes,
    ProfileActionResponse AddPasskey);
