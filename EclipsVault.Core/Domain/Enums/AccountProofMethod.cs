namespace EclipsVault.Core.Domain.Enums;

/// <summary>An existing credential a vault account can verify itself with before a security change.</summary>
public enum AccountProofMethod
{
    PasswordAndAuthenticatorCode = 1,
    PasswordAndRecoveryCode = 2,
    Passkey = 3
}
