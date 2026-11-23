using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Users;

/// <summary>
/// Activates an invited account for the holder of its emailed token. The token is only ever compared by its
/// hash; activation commits under the account's stamp, so an activation or resend that commits first wins.
/// </summary>
public sealed class AccountActivationService(
    IAccountInvitationRepository invitations,
    IPasswordHasher hasher,
    IBreachedPasswordScreen breachScreen,
    ITotpService totp,
    IAccountChangeCommitter changes,
    TimeProvider clock) : IAccountActivationService
{
    public async Task<ActivationInvitationDto?> InspectAsync(string token, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (await UsableAsync(token, now, ct) is not { } invitation)
        {
            return null;
        }

        // Stored only if no seed is stored yet and the token is still this one, so two inspections agree
        // and a seed shown under a replaced token never carries over to its replacement.
        var seed = invitation.PendingTotpSecret
                   ?? await invitations.StorePendingSecretOnceAsync(invitation.Id, invitation.TokenHash, totp.GenerateSecret(), now, ct);
        if (seed is null)
        {
            return null;
        }

        var user = invitation.User!;
        return new ActivationInvitationDto(user.Username, user.DisplayName, seed, totp.BuildOtpAuthUri(seed, user.Username), invitation.ExpiresAtUtc);
    }

    public async Task<ActivationOutcome> CompleteAsync(string token, string password, string code, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (await UsableAsync(token, now, ct) is not { } invitation)
        {
            return ActivationOutcome.InvitationInvalid;
        }

        if (PasswordRules.Refusal(password, breachScreen) is { } refusal)
        {
            throw new AccountInputException("Password", refusal);
        }

        var user = invitation.User!;
        if (invitation.PendingTotpSecret is not { } seed || !totp.TryValidateCode(seed, code, user.LastTotpStep, out var step))
        {
            return ActivationOutcome.CodeInvalid;
        }

        var hashed = hasher.Hash(password);
        user.PasswordHash = hashed.Hash;
        user.PasswordSalt = hashed.Salt;
        user.TotpSecret = seed;
        user.TotpEnabled = true;
        user.LastTotpStep = step;
        user.AwaitingActivation = false;
        // Nothing issued before activation, such as a pending sign-in from before the upgrade, survives it.
        user.CredentialGeneration++;
        invitation.SpentAtUtc = now;
        invitation.PendingTotpSecret = null;
        try
        {
            await changes.CommitAsync(user, AuditAction.AccountActivated,
                $"Account '{user.Username}' activated by invitation: password chosen and authenticator enrolled", ct);
        }
        catch (AccountChangedException)
        {
            // Another change to the account committed first: another activation or a resend, which both
            // retire this token, or an administrator's change, after which the link is asked for again.
            return ActivationOutcome.InvitationInvalid;
        }

        return ActivationOutcome.Activated;
    }

    private async Task<AccountInvitation?> UsableAsync(string token, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var invitation = await invitations.FindByTokenHashAsync(InvitationToken.Hash(token), ct);
        // A disabled account's invitation is not usable either: disabling an account stops its link.
        return invitation is { User: { AwaitingActivation: true, IsDisabled: false } } && invitation.IsUsable(now) ? invitation : null;
    }
}
