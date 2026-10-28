using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Profile;

/// <summary>
/// Commits a verified Profile operation: exactly the change that was prepared and verified, by the
/// session that did both, within five minutes of the verification. The operation is consumed in the
/// same transaction as the change, its credential generation and its one audit event, so a grant
/// authorizes one commit and nothing else.
/// </summary>
public sealed class VerifiedProfileChanges(
    IUserRepository users,
    IProfileOperationRepository operations,
    IMfaRecoveryCodeRepository recoveryCodes,
    IMfaRecoveryService recovery,
    IPasskeyCredentialRepository passkeyCredentials,
    IPasskeyService passkeys,
    IAccountChangeCommitter changes,
    INotificationService notifications,
    ISessionRevocationService revocation,
    TimeProvider clock)
{
    /// <returns>The credential generation the change committed.</returns>
    /// <exception cref="ProfileOperationRefusedException">The operation cannot be committed; nothing changed.</exception>
    public async Task<long> ChangePasswordAsync(ProfileSession session, Guid operationId, CancellationToken ct)
    {
        var (account, operation) = await TakeGrantAsync(session, operationId, ProfileOperationKind.ChangePassword, ct);
        operations.StageConsumption(operation);
        account.PasswordHash = operation.CandidatePasswordHash!;
        account.PasswordSalt = operation.CandidatePasswordSalt!;
        account.CredentialGeneration++;
        await CommitAsync(account, operation, AuditAction.PasswordChanged, "Password changed by owner after account verification; sessions revoked", ct);

        return account.CredentialGeneration;
    }

    /// <summary>Retires the authenticator, its replay marker and every recovery code. Every session ends.</summary>
    /// <exception cref="ProfileOperationRefusedException">The operation cannot be committed; nothing changed.</exception>
    public async Task ResetAuthenticatorAsync(ProfileSession session, Guid operationId, CancellationToken ct)
    {
        var (account, operation) = await TakeGrantAsync(session, operationId, ProfileOperationKind.ResetAuthenticator, ct);
        RequireAuthenticator(account);

        // Load the rows before changing the account, so a failed query leaves no reset staged.
        await recoveryCodes.StageRemovalOfAllAsync(account.Id, ct);
        operations.StageConsumption(operation);
        account.TotpSecret = null;
        account.TotpEnabled = false;
        account.LastTotpStep = null;
        account.CredentialGeneration++;
        await CommitAsync(account, operation, AuditAction.SelfMfaReset,
            "Authenticator reset by owner after account verification; re-enrollment required", ct);
    }

    /// <summary>Replaces every recovery code with ten new ones, returned here once and never stored in plaintext.</summary>
    /// <exception cref="ProfileOperationRefusedException">The operation cannot be committed; nothing changed.</exception>
    public async Task<RecoveryCodeSet> ReplaceRecoveryCodesAsync(ProfileSession session, Guid operationId, CancellationToken ct)
    {
        var (account, operation) = await TakeGrantAsync(session, operationId, ProfileOperationKind.ReplaceRecoveryCodes, ct);
        RequireAuthenticator(account);

        var codes = await recovery.StageNewSetAsync(account.Id, ct);
        operations.StageConsumption(operation);
        account.CredentialGeneration++;
        await CommitAsync(account, operation, AuditAction.RecoveryCodesGenerated,
            $"{codes.Count} single-use MFA recovery codes generated after account verification; any previous codes invalidated", ct);
        return new RecoveryCodeSet(codes, account.CredentialGeneration);
    }

    /// <summary>Issues the prompt that creates the passkey, replacing any earlier prompt of the operation.</summary>
    /// <returns>The WebAuthn creation options for the browser.</returns>
    /// <exception cref="ProfileOperationRefusedException">The operation cannot be used; nothing changed.</exception>
    public async Task<string> BeginPasskeyRegistrationAsync(ProfileSession session, Guid operationId, CancellationToken ct)
    {
        var (account, operation) = await TakeGrantAsync(session, operationId, ProfileOperationKind.AddPasskey, ct);
        var options = await passkeys.BeginRegistrationAsync(account.Id, ct);
        var deadline = clock.GetUtcNow() + ProfileOperationLimits.CeremonyLifetime;
        if (!await operations.IssueChallengeAsync(operation, PasskeyCeremonyPurpose.Registration, options.Challenge, deadline, ct))
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.OperationNotFound);
        }

        return options.OptionsJson;
    }

    /// <summary>Stores the new passkey under the name frozen when the operation was prepared.</summary>
    /// <exception cref="ProfileOperationRefusedException">The operation cannot be committed; nothing changed.</exception>
    public async Task<RegisteredPasskey> CompletePasskeyRegistrationAsync(
        ProfileSession session, Guid operationId, string responseJson, CancellationToken ct)
    {
        var (account, operation) = await TakeGrantAsync(session, operationId, ProfileOperationKind.AddPasskey, ct);
        var challenge = operation.ChallengePurpose == PasskeyCeremonyPurpose.Registration ? operation.Challenge : null;
        if (challenge is null || operation.ChallengeExpiresAtUtc <= clock.GetUtcNow())
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.CeremonyExpired);
        }

        var attestation = await passkeys.VerifyRegistrationAsync(account.Id, challenge, responseJson, operation.PasskeyNickname!, ct);
        if (attestation.Credential is not { } passkey)
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.InvalidInput, "credential", attestation.Error);
        }

        passkeyCredentials.StageAddition(passkey);
        operations.StageConsumption(operation);
        account.CredentialGeneration++;
        await CommitAsync(account, operation, AuditAction.PasskeyRegistered,
            $"Passkey '{passkey.Nickname}' registered after account verification", async token =>
            {
                if (!await operations.TryConsumeChallengeAsync(operation, PasskeyCeremonyPurpose.Registration, clock.GetUtcNow(), token))
                {
                    throw new ProfileOperationRefusedException(ProfileRefusal.CeremonyExpired);
                }
            }, ct);
        return new RegisteredPasskey(new PasskeySummary(passkey.Id, passkey.Nickname!, passkey.CreatedAtUtc), account.CredentialGeneration);
    }

    /// <summary>
    /// Removes the passkey the operation was prepared for. Removing the last one needs the method that
    /// verified it to keep working, checked again while the account is locked, so a recovery code
    /// spent by another request cannot leave the account with no usable method.
    /// </summary>
    /// <returns>The credential generation the removal committed.</returns>
    /// <exception cref="ProfileOperationRefusedException">The operation cannot be committed; nothing changed.</exception>
    public async Task<long> RemovePasskeyAsync(ProfileSession session, Guid operationId, Guid passkeyId, CancellationToken ct)
    {
        var (account, operation) = await TakeGrantAsync(session, operationId, ProfileOperationKind.RemovePasskey, ct);
        var passkey = await passkeyCredentials.FindByIdForUserAsync(passkeyId, account.Id, ct)
            ?? throw new ProfileOperationRefusedException(ProfileRefusal.PasskeyNotFound);
        if (operation.TargetPasskeyId != passkeyId)
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.OperationChanged);
        }

        var removesLast = (await passkeyCredentials.ListForUserAsync(account.Id, ct)).Count == 1;

        passkeyCredentials.StageRemoval(passkey);
        operations.StageConsumption(operation);
        account.CredentialGeneration++;
        await CommitAsync(account, operation, AuditAction.PasskeyRemoved,
            $"Passkey '{passkey.Nickname ?? "Passkey"}' removed after account verification", async token =>
            {
                if (!removesLast)
                {
                    return;
                }

                var remaining = new AccountCredentials(account.TotpEnabled, await recoveryCodes.CountUnusedAsync(account.Id, token), []);
                if (!remaining.KeepsWorkingWithoutPasskeys(operation.ProofMethod!.Value))
                {
                    throw new ProfileOperationRefusedException(ProfileRefusal.AlternateMethodRequired);
                }
            }, ct);
        return account.CredentialGeneration;
    }

    /// <summary>
    /// The session's verified operation of <paramref name="kind"/>, still within its grant and prepared
    /// under the credentials and sign-out state the account and the session still have.
    /// </summary>
    private async Task<(User Account, ProfileOperation Operation)> TakeGrantAsync(
        ProfileSession session, Guid operationId, ProfileOperationKind kind, CancellationToken ct)
    {
        var operation = await operations.FindAsync(operationId, session.AccountId, session.SessionId, ct)
            ?? throw new ProfileOperationRefusedException(ProfileRefusal.OperationNotFound);
        if (operation.Kind != kind)
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.OperationChanged);
        }

        if (operation.VerifiedAtUtc is not { } verifiedAt)
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.VerificationRequired);
        }

        if (clock.GetUtcNow() >= verifiedAt + ProfileOperationLimits.GrantLifetime)
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.VerificationExpired);
        }

        var account = await users.FindByIdAsync(session.AccountId, ct)
            ?? throw new ProfileOperationRefusedException(ProfileRefusal.OperationNotFound);
        if (account.CredentialGeneration != operation.CredentialGeneration
            || session.Stamp != new SessionStamp(operation.CredentialGeneration, operation.RevocationVersion))
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.OperationChanged);
        }

        return (account, operation);
    }

    private Task CommitAsync(User account, ProfileOperation operation, AuditAction action, string details, CancellationToken ct)
        => CommitAsync(account, operation, action, details, _ => Task.CompletedTask, ct);

    private async Task CommitAsync(User account, ProfileOperation operation, AuditAction action, string details,
        Func<CancellationToken, Task> recheck, CancellationToken ct)
    {
        var trustedAddress = account.Email;
        await changes.CommitAsync(account, action, details, async token =>
        {
            // Hashing or another account operation may have delayed this request after its first check.
            if (clock.GetUtcNow() >= operation.VerifiedAtUtc!.Value + ProfileOperationLimits.GrantLifetime)
            {
                throw new ProfileOperationRefusedException(ProfileRefusal.VerificationExpired);
            }

            if (await revocation.FindAsync(account.Id, token) != operation.RevocationVersion)
            {
                throw new ProfileOperationRefusedException(ProfileRefusal.OperationChanged);
            }

            await recheck(token);
        }, ct);
        await notifications.NotifySensitiveChangeAsync(trustedAddress, operation.Kind, CancellationToken.None);
    }

    private static void RequireAuthenticator(User account)
    {
        if (!account.TotpEnabled)
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.AuthenticatorNotEnrolled);
        }
    }
}
