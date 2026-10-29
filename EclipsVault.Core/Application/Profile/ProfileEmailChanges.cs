using System.Security.Cryptography;
using System.Text;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Profile;

public sealed record PendingEmail(Guid OperationId, string Address, DateTimeOffset ExpiresAtUtc);

public sealed class ProfileEmailChanges(
    IUserRepository users,
    IProfileOperationRepository operations,
    IAuthenticationAttempt attempt,
    IEmailVerificationSendLimits limits,
    IEmailVerificationDelivery delivery,
    ISessionRevocationService revocation,
    INotificationService notifications,
    TimeProvider clock)
{
    private const int CodeEntropyBytes = 16;
    private const int MaximumMailboxFailures = 5;
    private static readonly TimeSpan MailboxLifetime = TimeSpan.FromMinutes(15);

    public async Task<PendingEmail> RequestAsync(ProfileSession session, Guid operationId, string sourceIp, bool resend, CancellationToken ct)
    {
        RequireDelivery();
        var (account, operation) = await LoadAsync(session, operationId, ct);
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(CodeEntropyBytes));
        var hash = Hash(code);
        DateTimeOffset? grant = null;
        await attempt.RunAsync(account, async token =>
        {
            await RequireBoundAsync(session, account, operation, token);
            if (operation.MailboxExpiresAtUtc <= clock.GetUtcNow())
            {
                throw Refused(ProfileRefusal.EmailCodeExpired);
            }

            RequireGrant(operation);
            if (resend && operation.MailboxExpiresAtUtc is null)
            {
                throw Refused(ProfileRefusal.EmailCodeExpired);
            }

            if (await users.IsEmailTakenAsync(operation.TargetEmail!, account.Id, token))
            {
                throw Refused(ProfileRefusal.EmailConflict);
            }

            if (!await limits.TryReserveAsync(account.Id, sourceIp, clock.GetUtcNow(), token))
            {
                throw Refused(ProfileRefusal.Throttled);
            }

            grant = operation.VerifiedAtUtc;
            // Failure, process loss or a lost SMTP reply must leave no deliverable proof or old grant.
            operation.VerifiedAtUtc = null;
            operation.MailboxCodeHash = hash;
            operation.MailboxExpiresAtUtc = clock.GetUtcNow() + MailboxLifetime;
            operation.ExpiresAtUtc = operation.MailboxExpiresAtUtc.Value;
            operation.MailboxFailures = 0;
            operation.MailboxDelivered = false;
            attempt.RecordUserEvent(AuditAction.EmailChangeRequested, account, "Mailbox confirmation requested");
            return true;
        }, ct);

        if (!await delivery.TrySendAsync(operation.TargetEmail!, code, ct))
        {
            throw Refused(ProfileRefusal.MailUnavailable);
        }

        try
        {
            await attempt.RunAsync(account, async token =>
            {
                await RequireBoundAsync(session, account, operation, token);
                if (operation.MailboxCodeHash is null || !CryptographicOperations.FixedTimeEquals(hash, operation.MailboxCodeHash))
                {
                    throw Refused(ProfileRefusal.OperationChanged);
                }

                operation.MailboxDelivered = true;
                operation.VerifiedAtUtc ??= grant;
                return true;
            }, ct);
        }
        catch (SessionStoreUnavailableException)
        {
            // The first transaction and SMTP acceptance cannot be rolled back by this recheck.
            throw Refused(ProfileRefusal.EmailRequestIncomplete);
        }
        return new PendingEmail(operation.Id, operation.TargetEmail!, operation.MailboxExpiresAtUtc!.Value);
    }

    public async Task<long> ConfirmAsync(ProfileSession session, Guid operationId, string code, CancellationToken ct)
    {
        var (account, operation) = await LoadAsync(session, operationId, ct);
        string? oldEmail = null;
        var refusal = await attempt.RunAsync(account, async token =>
        {
            await RequireBoundAsync(session, account, operation, token);
            if (operation.MailboxCodeHash is null || clock.GetUtcNow() >= operation.MailboxExpiresAtUtc)
            {
                Invalidate(operation);
                return ProfileRefusal.EmailCodeExpired;
            }

            if (!operation.MailboxDelivered)
            {
                return ProfileRefusal.MailUnavailable;
            }

            RequireGrant(operation);
            if (!CryptographicOperations.FixedTimeEquals(Hash(code), operation.MailboxCodeHash))
            {
                operation.MailboxFailures++;
                if (operation.MailboxFailures >= MaximumMailboxFailures)
                {
                    Invalidate(operation);
                }

                return ProfileRefusal.EmailCodeInvalid;
            }

            if (await users.IsEmailTakenAsync(operation.TargetEmail!, account.Id, token))
            {
                return ProfileRefusal.EmailConflict;
            }

            oldEmail = account.Email;
            account.Email = operation.TargetEmail!;
            account.CredentialGeneration++;
            operations.StageConsumption(operation);
            attempt.RecordUserEvent(AuditAction.EmailChanged, account, "Email changed after account and mailbox verification; sessions revoked");
            return (ProfileRefusal?)null;
        }, ct);
        if (refusal is { } reason)
        {
            throw Refused(reason);
        }

        await notifications.NotifySensitiveChangeAsync(oldEmail!, ProfileOperationKind.ChangeEmail, CancellationToken.None);
        return account.CredentialGeneration;
    }

    public async Task<bool> CancelAsync(ProfileSession session, Guid operationId, CancellationToken ct)
    {
        await LoadAsync(session, operationId, ct);
        return await operations.DeleteAsync(operationId, session.AccountId, session.SessionId, ct);
    }

    private async Task<(User Account, ProfileOperation Operation)> LoadAsync(ProfileSession session, Guid operationId, CancellationToken ct)
    {
        var operation = await operations.FindAsync(operationId, session.AccountId, session.SessionId, ct);
        var account = await users.FindByIdAsync(session.AccountId, ct);
        return operation?.Kind == ProfileOperationKind.ChangeEmail && account is not null
            ? (account, operation) : throw Refused(ProfileRefusal.OperationNotFound);
    }

    private async Task RequireBoundAsync(ProfileSession session, User account, ProfileOperation operation, CancellationToken ct)
    {
        if (!await operations.RefreshAsync(operation, ct))
        {
            throw Refused(ProfileRefusal.OperationNotFound);
        }

        if (account.IsDisabled || account.CredentialGeneration != operation.CredentialGeneration
            || session.Stamp != new SessionStamp(operation.CredentialGeneration, operation.RevocationVersion)
            || await revocation.FindAsync(account.Id, ct) != operation.RevocationVersion)
        {
            throw Refused(ProfileRefusal.OperationChanged);
        }
    }

    private void RequireGrant(ProfileOperation operation)
    {
        if (operation.VerifiedAtUtc is not { } verified)
        {
            throw Refused(ProfileRefusal.VerificationRequired);
        }

        if (clock.GetUtcNow() >= verified + ProfileOperationLimits.GrantLifetime)
        {
            throw Refused(ProfileRefusal.VerificationExpired);
        }
    }

    private void RequireDelivery()
    {
        if (!delivery.Available)
        {
            throw Refused(ProfileRefusal.MailUnavailable);
        }
    }

    private static void Invalidate(ProfileOperation operation)
    {
        operation.MailboxCodeHash = null;
        operation.MailboxDelivered = false;
        operation.VerifiedAtUtc = null;
        operation.ProofMethod = null;
        operation.ExpiresAtUtc = DateTimeOffset.MinValue;
    }

    private static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));

    private static ProfileOperationRefusedException Refused(ProfileRefusal refusal) => new(refusal);
}
