using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Profile;

/// <summary>
/// Prepares a Profile security change: checks it, freezes exactly what it will change and stores it
/// for the session that asked, before any credential is proved for it (ADR-0022). Preparing changes
/// nothing about the account and records no event.
/// </summary>
public sealed class ProfileOperationPreparation(
    IUserRepository users,
    IProfileOperationRepository operations,
    IMfaRecoveryCodeRepository recoveryCodes,
    IPasskeyCredentialRepository passkeys,
    IBreachedPasswordScreen breachScreen,
    CandidatePasswordHashing hashing,
    TimeProvider clock)
{
    private static readonly ProfileOperationKind[] AccountWideChanges =
    [
        ProfileOperationKind.ChangePassword,
        ProfileOperationKind.ResetAuthenticator,
        ProfileOperationKind.ReplaceRecoveryCodes,
        ProfileOperationKind.AddPasskey,
    ];

    public async Task<ProfileSecurity> DescribeAsync(Guid accountId, CancellationToken ct)
    {
        var credentials = await CredentialsOfAsync(accountId, ct);
        return new ProfileSecurity(
            credentials.MethodsFor(ProfileOperationKind.ChangePassword, null),
            AccountWideChanges.ToDictionary(kind => kind, kind => credentials.RefusalFor(kind, null)),
            credentials.PasskeyIds.ToDictionary(id => id, id => credentials.RefusalFor(ProfileOperationKind.RemovePasskey, id)));
    }

    /// <summary>Stores the change as the session's only prepared operation, replacing the one it had.</summary>
    /// <exception cref="ProfileOperationRefusedException">The change is invalid or unavailable; nothing was stored.</exception>
    public async Task<PreparedOperation> PrepareAsync(ProfileSession session, ProfileChangeRequest change, CancellationToken ct)
    {
        // Cheap checks first: the password hash below is the expensive part.
        RejectFieldsOfOtherKinds(change);
        if (change.Kind == ProfileOperationKind.ChangePassword)
        {
            RejectWeakPassword(change.NewPassword);
        }

        var account = await users.FindByIdAsync(session.AccountId, ct);
        if (account is null || account.CredentialGeneration != session.Stamp.CredentialGeneration)
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.OperationChanged);
        }

        var credentials = await CredentialsOfAsync(account.Id, ct);
        if (credentials.RefusalFor(change.Kind, change.PasskeyId) is { } refusal)
        {
            throw new ProfileOperationRefusedException(refusal);
        }

        var now = clock.GetUtcNow();
        if (await operations.CountLiveAsync(account.Id, session.SessionId, now, ct) >= ProfileOperationLimits.LivePerAccount)
        {
            throw new ProfileOperationRefusedException(ProfileRefusal.Throttled);
        }

        var operation = new ProfileOperation
        {
            Id = Guid.NewGuid(),
            UserId = account.Id,
            SessionId = session.SessionId,
            CredentialGeneration = session.Stamp.CredentialGeneration,
            RevocationVersion = session.Stamp.RevocationVersion,
            Kind = change.Kind,
            PasskeyNickname = change.Kind == ProfileOperationKind.AddPasskey ? PasskeyNickname.Normalize(change.PasskeyNickname) : null,
            TargetPasskeyId = change.PasskeyId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + ProfileOperationLimits.VerificationWindow,
        };
        if (change.Kind == ProfileOperationKind.ChangePassword)
        {
            var candidate = await hashing.TryHashAsync(change.NewPassword!, ct)
                ?? throw new ProfileOperationRefusedException(ProfileRefusal.Throttled);
            operation.CandidatePasswordHash = candidate.Hash;
            operation.CandidatePasswordSalt = candidate.Salt;
        }

        await operations.ReplaceForSessionAsync(operation, now, ct);
        return new PreparedOperation(operation.Id, operation.Kind, operation.ExpiresAtUtc,
            credentials.MethodsFor(operation.Kind, operation.TargetPasskeyId));
    }

    /// <returns>False when the session had no such operation.</returns>
    public Task<bool> CancelAsync(ProfileSession session, Guid operationId, CancellationToken ct)
        => operations.DeleteAsync(operationId, session.AccountId, session.SessionId, ct);

    private async Task<AccountCredentials> CredentialsOfAsync(Guid accountId, CancellationToken ct)
    {
        var account = await users.FindByIdAsync(accountId, ct);
        var keys = await passkeys.ListForUserAsync(accountId, ct);
        return new AccountCredentials(
            account?.TotpEnabled == true,
            await recoveryCodes.CountUnusedAsync(accountId, ct),
            keys.Select(key => key.Id).ToList());
    }

    private static void RejectFieldsOfOtherKinds(ProfileChangeRequest change)
    {
        if (change.NewPassword is not null && change.Kind != ProfileOperationKind.ChangePassword)
        {
            throw Invalid("newPassword", "Only a password change takes a new password.");
        }

        if (change.PasskeyNickname is not null && change.Kind != ProfileOperationKind.AddPasskey)
        {
            throw Invalid("passkeyNickname", "Only adding a passkey takes a name.");
        }

        if (change.PasskeyId is not null && change.Kind != ProfileOperationKind.RemovePasskey)
        {
            throw Invalid("passkeyId", "Only removing a passkey names one.");
        }

        if (change.PasskeyId is null && change.Kind == ProfileOperationKind.RemovePasskey)
        {
            throw Invalid("passkeyId", "Choose the passkey to remove.");
        }
    }

    private void RejectWeakPassword(string? password)
    {
        if (password is null || password.Length < PasswordRules.MinimumLength)
        {
            throw Invalid("newPassword", $"Your new password must be at least {PasswordRules.MinimumLength} characters long.");
        }

        if (password.Length > PasswordRules.MaximumLength)
        {
            throw Invalid("newPassword", $"Your new password must be at most {PasswordRules.MaximumLength} characters long.");
        }

        if (breachScreen.IsCompromised(password))
        {
            throw Invalid("newPassword", "This password has appeared in a known data breach. Choose a different one.");
        }
    }

    private static ProfileOperationRefusedException Invalid(string field, string message)
        => new(ProfileRefusal.InvalidInput, field, message);
}
