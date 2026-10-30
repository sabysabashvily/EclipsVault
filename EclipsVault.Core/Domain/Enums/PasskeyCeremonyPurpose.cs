namespace EclipsVault.Core.Domain.Enums;

/// <summary>What a passkey challenge was issued for, so it answers nothing else.</summary>
public enum PasskeyCeremonyPurpose
{
    AccountVerification = 1,
    Registration = 2,
    SignIn = 3
}
