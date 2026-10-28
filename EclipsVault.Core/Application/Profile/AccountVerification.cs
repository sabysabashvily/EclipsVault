using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Profile;

/// <summary>
/// Proves an existing credential for one prepared Profile operation (ADR-0022): the current password
/// with an authenticator or unused recovery code, or one of the account's own passkeys. Attempts share
/// the account's lockout with sign-in, and the codes they spend, the counters they move and the events
/// they record commit together. A success authorizes that operation alone, for five minutes; it is
/// recorded as a verification, never as a sign-in.
/// </summary>
public sealed class AccountVerification(
    IUserRepository users,
    IProfileOperationRepository operations,
    IMfaRecoveryCodeRepository recoveryCodes,
    IPasskeyCredentialRepository passkeyCredentials,
    IPasskeyService passkeys,
    IPasswordHasher hasher,
    SecondFactorVerifier secondFactors,
    IAuthenticationAttempt attempt,
    AuthenticationFailures failures,
    ISessionRevocationService revocation,
    TimeProvider clock)
{
    /// <exception cref="ProfileOperationRefusedException">The proof was wrong or the operation cannot be verified.</exception>
    public async Task<VerifiedOperation> VerifyWithCodeAsync(
        ProfileSession session, Guid operationId, AccountProofMethod method, string currentPassword, string code, CancellationToken ct)
    {
        if (method == AccountProofMethod.Passkey)
        {
            throw Invalid("method", "A passkey verifies through its own prompt.");
        }

        var (account, operation) = await LoadAsync(session, operationId, ct);
        var refusal = await attempt.RunAsync(account, async token =>
        {
            await RequireVerifiableAsync(account, operation, method, token);
            if (Blocked(account) is { } blocked)
            {
                return blocked;
            }

            if (!hasher.Verify(currentPassword, account.PasswordHash, account.PasswordSalt))
            {
                return await WrongProofAsync(account, "Account verification failed: wrong password", token);
            }

            // Before the code is spent, so a refused change does not use up a recovery code.
            if (UnchangedPassword(operation, currentPassword) is { } unchanged)
            {
                return unchanged;
            }

            var accepted = method == AccountProofMethod.PasswordAndAuthenticatorCode
                ? await secondFactors.TryAcceptAuthenticatorCodeAsync(account, code, token)
                : await secondFactors.TryRedeemRecoveryCodeAsync(account, code, token);
            if (!accepted)
            {
                return await WrongProofAsync(account, method == AccountProofMethod.PasswordAndAuthenticatorCode
                    ? "Account verification failed: wrong authenticator code"
                    : "Account verification failed: wrong or used recovery code", token);
            }

            return await GrantAsync(account, operation, method, token);
        }, ct);

        return refusal is null ? Verified(operation) : throw refusal;
    }

    /// <summary>Issues the passkey prompt for the operation, replacing any earlier prompt it had.</summary>
    /// <returns>The WebAuthn assertion options for the browser.</returns>
    public async Task<string> BeginPasskeyAsync(ProfileSession session, Guid operationId, CancellationToken ct)
    {
        var (account, operation) = await LoadAsync(session, operationId, ct);
        await RequireVerifiableAsync(account, operation, AccountProofMethod.Passkey, ct);

        var options = await passkeys.BeginAccountVerificationAsync(account.Id, operation.TargetPasskeyId, ct);
        var deadline = clock.GetUtcNow() + ProfileOperationLimits.CeremonyLifetime;
        if (!await operations.IssueChallengeAsync(operation, PasskeyCeremonyPurpose.AccountVerification, options.Challenge, deadline, ct))
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.OperationNotFound);
        }

        return options.OptionsJson;
    }

    /// <summary>
    /// Verifies the operation with the passkey's answer to its prompt. A password change also needs
    /// <paramref name="currentPassword"/>, and both must be right before earlier failures are forgiven.
    /// </summary>
    /// <exception cref="ProfileOperationRefusedException">The proof was wrong or the operation cannot be verified.</exception>
    public async Task<VerifiedOperation> VerifyWithPasskeyAsync(
        ProfileSession session, Guid operationId, string responseJson, string? currentPassword, CancellationToken ct)
    {
        var (account, operation) = await LoadAsync(session, operationId, ct);
        if (operation.Kind == ProfileOperationKind.ChangePassword && string.IsNullOrEmpty(currentPassword))
        {
            throw Invalid("currentPassword", "Enter your current password.");
        }

        var challenge = operation.ChallengePurpose == PasskeyCeremonyPurpose.AccountVerification ? operation.Challenge : null;
        if (challenge is null)
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.CeremonyExpired);
        }

        var refusal = await attempt.RunAsync(account, async token =>
        {
            await RequireVerifiableAsync(account, operation, AccountProofMethod.Passkey, token);
            if (operation.Challenge != challenge
                || !await operations.TryConsumeChallengeAsync(operation, PasskeyCeremonyPurpose.AccountVerification, clock.GetUtcNow(), token))
            {
                throw new ProfileOperationRefusedException(ProfileRefusal.CeremonyExpired);
            }

            if (Blocked(account) is { } blocked)
            {
                return blocked;
            }

            if (operation.Kind == ProfileOperationKind.ChangePassword)
            {
                if (!hasher.Verify(currentPassword!, account.PasswordHash, account.PasswordSalt))
                {
                    return await WrongProofAsync(account, "Account verification failed: wrong password", token);
                }

                if (UnchangedPassword(operation, currentPassword!) is { } unchanged)
                {
                    return unchanged;
                }
            }

            var proof = await passkeys.VerifyAccountAssertionAsync(account, challenge, responseJson, operation.TargetPasskeyId, token);
            if (proof.Outcome == PasskeyProofOutcome.Malformed)
            {
                return Invalid("credential", "The passkey response could not be read.");
            }

            if (proof.Outcome == PasskeyProofOutcome.Rejected)
            {
                return await WrongProofAsync(account, proof.Rejection!, token);
            }

            return await GrantAsync(account, operation, AccountProofMethod.Passkey, token);
        }, ct);

        return refusal is null ? Verified(operation) : throw refusal;
    }

    private async Task<(User Account, ProfileOperation Operation)> LoadAsync(ProfileSession session, Guid operationId, CancellationToken ct)
    {
        var operation = await operations.FindAsync(operationId, session.AccountId, session.SessionId, ct);
        var account = operation is null ? null : await users.FindByIdAsync(session.AccountId, ct);
        return account is null || operation is null
            ? throw new ProfileOperationRefusedException(ProfileRefusal.OperationNotFound)
            : (account, operation);
    }

    /// <summary>
    /// Refuses an operation that is gone, already verified, out of time or prepared under credentials
    /// or a sign-out state the account has moved on from, and a method that cannot verify it.
    /// </summary>
    private async Task RequireVerifiableAsync(User account, ProfileOperation operation, AccountProofMethod method, CancellationToken ct)
    {
        if (!await operations.RefreshAsync(operation, ct))
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.OperationNotFound);
        }

        if (operation.VerifiedAtUtc is { } verifiedAt
            && (operation.Kind != ProfileOperationKind.ChangeEmail || clock.GetUtcNow() < verifiedAt + ProfileOperationLimits.GrantLifetime))
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.OperationChanged, message: "This change was already verified.");
        }

        if (clock.GetUtcNow() >= operation.ExpiresAtUtc)
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.OperationExpired);
        }

        if (account.CredentialGeneration != operation.CredentialGeneration
            || await revocation.FindAsync(account.Id, ct) != operation.RevocationVersion)
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.OperationChanged);
        }

        var keys = await passkeyCredentials.ListForUserAsync(account.Id, ct);
        var credentials = new AccountCredentials(
            account.TotpEnabled, await recoveryCodes.CountUnusedAsync(account.Id, ct), keys.Select(key => key.Id).ToList());
        if (!credentials.MethodsFor(operation.Kind, operation.TargetPasskeyId).Contains(method))
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.MethodUnavailable);
        }
    }

    private ProfileOperationRefusedException? Blocked(User account)
    {
        if (!account.IsDisabled && !account.IsLockedOut(clock.GetUtcNow()))
        {
            return null;
        }

        attempt.RecordUserEvent(AuditAction.AccountVerificationFailed, account, "Account is disabled or locked out");
        return new ProfileOperationRefusedException(ProfileRefusal.Throttled);
    }

    private async Task<ProfileOperationRefusedException> WrongProofAsync(User account, string reason, CancellationToken ct)
    {
        await failures.RecordAsync(account, AuditAction.AccountVerificationFailed, reason, ct);
        return new ProfileOperationRefusedException(ProfileRefusal.VerificationFailed);
    }

    /// <summary>A proved current password that is also the new one ends the operation: there is nothing to change.</summary>
    private ProfileOperationRefusedException? UnchangedPassword(ProfileOperation operation, string currentPassword)
    {
        if (operation.Kind != ProfileOperationKind.ChangePassword
            || !hasher.Verify(currentPassword, operation.CandidatePasswordHash!, operation.CandidatePasswordSalt!))
        {
            return null;
        }

        operations.StageConsumption(operation);
        return Invalid("newPassword", "Your new password must be different from the current one.");
    }

    private async Task<ProfileOperationRefusedException?> GrantAsync(
        User account, ProfileOperation operation, AccountProofMethod method, CancellationToken ct)
    {
        await failures.ResetAsync(account, ct);
        operation.VerifiedAtUtc = clock.GetUtcNow();
        operation.ProofMethod = method;
        attempt.RecordUserEvent(AuditAction.AccountVerified, account, $"Verified with {Describe(method)} to {Describe(operation.Kind)}");
        return null;
    }

    private static VerifiedOperation Verified(ProfileOperation operation)
        => new(operation.Id, operation.VerifiedAtUtc!.Value + ProfileOperationLimits.GrantLifetime);

    private static ProfileOperationRefusedException Invalid(string field, string message)
        => new(ProfileRefusal.InvalidInput, field, message);

    private static string Describe(AccountProofMethod method) => method switch
    {
        AccountProofMethod.PasswordAndAuthenticatorCode => "password and authenticator code",
        AccountProofMethod.PasswordAndRecoveryCode => "password and recovery code",
        _ => "a passkey"
    };

    private static string Describe(ProfileOperationKind kind) => kind switch
    {
        ProfileOperationKind.ChangeEmail => "change email",
        ProfileOperationKind.ChangePassword => "change the password",
        ProfileOperationKind.ResetAuthenticator => "reset the authenticator",
        ProfileOperationKind.ReplaceRecoveryCodes => "replace the recovery codes",
        ProfileOperationKind.AddPasskey => "add a passkey",
        _ => "remove a passkey"
    };
}
