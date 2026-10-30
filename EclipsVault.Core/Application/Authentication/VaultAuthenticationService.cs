using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Authentication;

/// <summary>
/// Password (Argon2id) + TOTP verification workflow. Authentication events commit with their
/// associated replay and lockout bookkeeping in one transaction.
/// An unknown username has no account to keep books for, so it is audited through <see cref="IAuditSink"/>.
/// Each later step checks the pending sign-in's credential generation after the attempt reloads the
/// account under its lock, so a credential change cannot commit between the check and the step.
/// </summary>
public sealed class VaultAuthenticationService : IVaultAuthenticationService
{
    // Verified against when the username is unknown, so response timing does not
    // reveal whether an account exists.
    private static readonly byte[] DummyHash = new byte[32];
    private static readonly byte[] DummySalt = new byte[16];

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITotpService _totp;
    private readonly IMfaRecoveryCodeRepository _recoveryCodes;
    private readonly SecondFactorVerifier _secondFactors;
    private readonly AuthenticationFailures _failures;
    private readonly IAuditSink _audit;
    private readonly IAuthenticationAttempt _attempt;
    private readonly ISessionRevocationService _revocation;
    private readonly TimeProvider _clock;

    public VaultAuthenticationService(
        IUserRepository users,
        IPasswordHasher hasher,
        ITotpService totp,
        IMfaRecoveryCodeRepository recoveryCodes,
        SecondFactorVerifier secondFactors,
        AuthenticationFailures failures,
        IAuditSink audit,
        IAuthenticationAttempt attempt,
        ISessionRevocationService revocation,
        TimeProvider clock)
    {
        _users = users;
        _hasher = hasher;
        _totp = totp;
        _recoveryCodes = recoveryCodes;
        _secondFactors = secondFactors;
        _failures = failures;
        _audit = audit;
        _attempt = attempt;
        _revocation = revocation;
        _clock = clock;
    }

    private Task AuditUserAsync(AuditAction action, Guid? userId, string username, string? details, CancellationToken ct)
        => _audit.WriteUserEventAsync(action, userId, username, details, ct);

    public async Task<CredentialCheckResult> ValidateCredentialsAsync(string usernameOrEmail, string password, CancellationToken ct)
    {
        var user = await _users.FindByUsernameOrEmailAsync(usernameOrEmail, ct);
        if (user is null)
        {
            _hasher.Verify(password, DummyHash, DummySalt);
            await AuditUserAsync(AuditAction.LoginFailed, null, usernameOrEmail, "Unknown username or email", ct);
            return CredentialCheckResult.Invalid;
        }

        // Captured before the attempt reloads the account: an administrator who changes its access
        // and signs it out after this point leaves this sign-in holding a version that is no longer current.
        var revocationVersion = await _revocation.CaptureAsync(user.Id, ct);
        return await _attempt.RunAsync(user, async token =>
        {
            if (user.IsLockedOut(_clock.GetUtcNow()))
            {
                _attempt.RecordUserEvent(AuditAction.LoginFailed, user, "Account is locked out");
                return CredentialCheckResult.Invalid;
            }

            if (!_hasher.Verify(password, user.PasswordHash, user.PasswordSalt))
            {
                await _failures.RecordAsync(user, AuditAction.LoginFailed, "Password verification failed", token);
                return CredentialCheckResult.Invalid;
            }

            // Disabled accounts fail after the password check so timing does not reveal the state.
            if (user.IsDisabled)
            {
                _attempt.RecordUserEvent(AuditAction.LoginFailed, user, "Account is disabled");
                return CredentialCheckResult.Invalid;
            }

            var status = user.TotpEnabled ? CredentialStatus.RequiresTotp : CredentialStatus.RequiresTotpEnrollment;
            return new CredentialCheckResult(status, UserDto.From(user), new SessionStamp(user.CredentialGeneration, revocationVersion));
        }, ct);
    }

    public Task<UserDto?> VerifyTotpAsync(Guid userId, SessionStamp stamp, string code, CancellationToken ct)
        => VerifyAuthenticatorAsync(userId, stamp, code, administratorRequired: false, ct);

    public Task<UserDto?> VerifyBreakGlassAsync(Guid userId, SessionStamp stamp, string code, CancellationToken ct)
        => VerifyAuthenticatorAsync(userId, stamp, code, administratorRequired: true, ct);

    private async Task<UserDto?> VerifyAuthenticatorAsync(
        Guid userId, SessionStamp stamp, string code, bool administratorRequired, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return null;
        }

        return await _attempt.RunAsync<UserDto?>(user, async token =>
        {
            await RequireCurrentProofAsync(user, stamp, ct);
            if (user.IsDisabled || !user.TotpEnabled || user.TotpSecret is null)
            {
                return null;
            }

            if (administratorRequired && user.Clearance != ClearanceLevel.TopSecret)
            {
                _attempt.RecordUserEvent(AuditAction.LoginFailed, user, "Break-glass recovery denied: insufficient clearance");
                return null;
            }

            if (user.IsLockedOut(_clock.GetUtcNow()))
            {
                _attempt.RecordUserEvent(AuditAction.TotpFailed, user, "Account is locked out");
                return null;
            }

            // Replaying an observed code is no cheaper than guessing: both count as failures.
            if (!await _secondFactors.TryAcceptAuthenticatorCodeAsync(user, code, token))
            {
                await _failures.RecordAsync(user, AuditAction.TotpFailed, "TOTP verification failed", token);
                return null;
            }

            await _failures.ResetAsync(user, token);
            _attempt.RecordUserEvent(AuditAction.LoginSucceeded, user, "Password + TOTP");
            return UserDto.From(user);
        }, ct);
    }

    public async Task<UserDto?> VerifyRecoveryCodeAsync(Guid userId, SessionStamp stamp, string code, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return null;
        }

        return await _attempt.RunAsync<UserDto?>(user, async token =>
        {
            await RequireCurrentProofAsync(user, stamp, ct);
            if (user.IsDisabled || !user.TotpEnabled)
            {
                return null;
            }

            if (user.IsLockedOut(_clock.GetUtcNow()))
            {
                _attempt.RecordUserEvent(AuditAction.TotpFailed, user, "Account is locked out");
                return null;
            }

            if (!await _secondFactors.TryRedeemRecoveryCodeAsync(user, code, token))
            {
                await _failures.RecordAsync(user, AuditAction.TotpFailed, "Recovery code verification failed", token);
                return null;
            }

            await _failures.ResetAsync(user, token);
            var remaining = await _recoveryCodes.CountUnusedAsync(userId, token);
            _attempt.RecordUserEvent(AuditAction.RecoveryCodeUsed, user, $"Recovery code redeemed; {remaining} remaining");
            _attempt.RecordUserEvent(AuditAction.LoginSucceeded, user, "Password + recovery code");
            return UserDto.From(user);
        }, ct);
    }

    public async Task<TotpEnrollmentDto> BeginTotpEnrollmentAsync(Guid userId, SessionStamp stamp, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new TotpEnrollmentException($"User '{userId}' was not found.");
        await RequireCurrentProofAsync(user, stamp, ct);

        if (user.TotpEnabled)
        {
            throw new TotpEnrollmentException("TOTP is already enrolled for this account.");
        }

        // Idempotent until enrollment completes: keep the pending secret stable so a
        // mistyped confirmation code does not silently rotate it.
        if (string.IsNullOrEmpty(user.TotpSecret))
        {
            user.TotpSecret = _totp.GenerateSecret();
            await _users.UpdateAsync(user, ct);
        }

        return new TotpEnrollmentDto(user.TotpSecret, _totp.BuildOtpAuthUri(user.TotpSecret, user.Username));
    }

    public async Task<UserDto?> CompleteTotpEnrollmentAsync(Guid userId, SessionStamp stamp, string code, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return null;
        }

        return await _attempt.RunAsync<UserDto?>(user, async token =>
        {
            await RequireCurrentProofAsync(user, stamp, ct);
            if (user.IsDisabled || user.TotpEnabled || string.IsNullOrEmpty(user.TotpSecret))
            {
                return null;
            }

            if (user.IsLockedOut(_clock.GetUtcNow()))
            {
                _attempt.RecordUserEvent(AuditAction.TotpFailed, user, "Account is locked out");
                return null;
            }

            // Enrollment spends the confirming step so it cannot also authorize the next sign-in.
            if (!_totp.TryValidateCode(user.TotpSecret, code, user.LastTotpStep, out var step)
                || !await _users.TryCompleteTotpEnrollmentAsync(user, step, token))
            {
                await _failures.RecordAsync(user, AuditAction.TotpFailed, "Enrollment confirmation failed", token);
                return null;
            }

            _attempt.RecordUserEvent(AuditAction.TotpEnrolled, user, null);
            _attempt.RecordUserEvent(AuditAction.LoginSucceeded, user, "Password + TOTP enrollment");
            return UserDto.From(user);
        }, ct);
    }
    private async Task RequireCurrentProofAsync(User user, SessionStamp stamp, CancellationToken ct)
    {
        user.RequireCredentialGeneration(stamp.CredentialGeneration);
        if (await _revocation.FindAsync(user.Id, ct) != stamp.RevocationVersion)
        {
            throw new StaleProofException();
        }
    }

}
