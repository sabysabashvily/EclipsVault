namespace EclipsVault.Core.Domain.Enums;

/// <summary>A security change to a vault account that needs account verification first (ADR-0022).</summary>
public enum ProfileOperationKind
{
    ChangePassword = 1,
    ResetAuthenticator = 2,
    ReplaceRecoveryCodes = 3,
    AddPasskey = 4,
    RemovePasskey = 5,
    ChangeEmail = 6
}
