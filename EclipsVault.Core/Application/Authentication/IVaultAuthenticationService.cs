
namespace EclipsVault.Core.Application.Authentication;

/// <summary>
/// Multi-factor authentication workflow: Argon2id password check first, then TOTP
/// (or TOTP enrollment on first sign-in). A full session principal may only be
/// issued after the second factor succeeds.
/// </summary>
public interface IVaultAuthenticationService
{
    Task<CredentialCheckResult> ValidateCredentialsAsync(string username, string password, CancellationToken ct);

    /// <summary>Returns the user when the code is valid, null otherwise (the failure is audited).</summary>
    Task<UserDto?> VerifyTotpAsync(Guid userId, string code, CancellationToken ct);

    Task<UserDto?> VerifyBreakGlassAsync(Guid userId, string code, CancellationToken ct);

    /// <summary>
    /// Redeems a single-use MFA recovery code in place of the TOTP step. On success the
    /// matched code is permanently consumed and the user is returned; a non-match is
    /// audited and counts toward lockout, returning null.
    /// </summary>
    Task<UserDto?> VerifyRecoveryCodeAsync(Guid userId, string code, CancellationToken ct);

    /// <summary>Idempotent until enrollment completes: repeated calls return the same pending secret.</summary>
    Task<TotpEnrollmentDto> BeginTotpEnrollmentAsync(Guid userId, CancellationToken ct);

    Task<UserDto?> CompleteTotpEnrollmentAsync(Guid userId, string code, CancellationToken ct);
}
