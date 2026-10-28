using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Notifications;

/// <summary>
/// Composes notification emails for domain events, delivers them through the configured
/// <see cref="Abstractions.IEmailSender"/>, and records every one to the outbox. Deliberately
/// <b>fail-soft</b>: a notification must never break the operation that triggered it, so all
/// exceptions are swallowed — a failed send is captured as a Failed outbox row, not thrown.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private const int MaxBodyLength = 4000;

    private readonly IEmailSender _sender;
    private readonly IEmailLogRepository _log;
    private readonly IUserRepository _users;
    private readonly NotificationOptions _options;
    private readonly TimeProvider _clock;

    public NotificationService(
        IEmailSender sender,
        IEmailLogRepository log,
        IUserRepository users,
        NotificationOptions options,
        TimeProvider clock)
    {
        _sender = sender;
        _log = log;
        _users = users;
        _options = options;
        _clock = clock;
    }

    public Task NotifyAccessRequestDecidedAsync(
        Guid requesterUserId, string secretName, bool approved, string reviewer, string? note, CancellationToken ct)
        => SafeSendAsync(async () =>
        {
            var user = await _users.FindByIdAsync(requesterUserId, ct);
            if (user is null)
            {
                return null;
            }

            var verb = approved ? "approved" : "declined";
            var body = $"Hello {DisplayName(user)},\n\n" +
                       $"Your request for access to '{secretName}' was {verb} by {reviewer}.\n" +
                       (approved
                           ? "You can now open it from the vault (clearance, network, and time rules still apply)."
                           : "You do not have access to this secret.") +
                       (string.IsNullOrWhiteSpace(note) ? "" : $"\n\nReviewer note: {note}") +
                       "\n\n— EclipsVault";

            return new Draft(user.Email, $"Your access request was {verb}",
                body, approved ? "AccessRequestApproved" : "AccessRequestRejected");
        }, ct);

    public Task NotifySensitiveChangeAsync(string trustedAddress, ProfileOperationKind kind, CancellationToken ct)
        => SafeSendAsync(() => Task.FromResult<Draft?>(new Draft(trustedAddress,
            "Your EclipsVault security settings changed",
            "A sensitive change was completed on your EclipsVault account. If this wasn't you, contact an administrator immediately.",
            kind == ProfileOperationKind.ChangePassword ? "PasswordChanged" : kind.ToString())), ct);

    public Task NotifyPasswordChangedAsync(Guid userId, CancellationToken ct)
        => SafeSendAsync(async () =>
        {
            var user = await _users.FindByIdAsync(userId, ct);
            if (user is null)
            {
                return null;
            }

            var body = $"Hello {DisplayName(user)},\n\n" +
                       "The password on your EclipsVault account was just changed. If this wasn't you, " +
                       "contact an administrator immediately and reset your credentials.\n\n— EclipsVault";
            return new Draft(user.Email, "Your EclipsVault password was changed", body, "PasswordChanged");
        }, ct);

    public Task NotifyCredentialChangedAsync(Guid userId, ProfileOperationKind kind, CancellationToken ct)
        => SafeSendAsync(async () =>
        {
            var user = await _users.FindByIdAsync(userId, ct);
            if (user is null)
            {
                return null;
            }

            var change = kind switch
            {
                ProfileOperationKind.ResetAuthenticator => "authenticator was reset",
                ProfileOperationKind.ReplaceRecoveryCodes => "recovery codes were replaced",
                ProfileOperationKind.AddPasskey => "passkey was added",
                ProfileOperationKind.RemovePasskey => "passkey was removed",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            var subject = $"Your EclipsVault {change}";
            var body = $"Hello {DisplayName(user)},\n\nYour EclipsVault {change}. " +
                "If this wasn't you, contact an administrator immediately and reset your credentials.\n\n— EclipsVault";
            return new Draft(user.Email, subject, body, kind.ToString());
        }, ct);

    public Task NotifyUserProvisionedAsync(string email, string displayName, string username, CancellationToken ct)
        => SafeSendAsync(() =>
        {
            var body = $"Hello {displayName},\n\n" +
                       "An EclipsVault account has been created for you.\n" +
                       $"Sign in with the username '{username}' (or this email) and the password an administrator gave you; " +
                       "you'll enrol an authenticator on first sign-in.\n\n— EclipsVault";
            return Task.FromResult<Draft?>(new Draft(email, "Your EclipsVault account is ready", body, "UserProvisioned"));
        }, ct);

    public Task<bool> NotifyExpiringSecretAsync(
        Guid ownerUserId, string secretName, DateTimeOffset expiresAtUtc, CancellationToken ct)
        => SafeSendAsync(async () =>
        {
            var user = await _users.FindByIdAsync(ownerUserId, ct);
            if (user is null)
            {
                return null;
            }

            var days = Math.Max(0, (int)Math.Ceiling((expiresAtUtc - _clock.GetUtcNow()).TotalDays));
            var body = $"Hello {DisplayName(user)},\n\n" +
                       $"The secret '{secretName}' expires on {expiresAtUtc:u} — about {days} day(s) from now.\n" +
                       "Once the deadline passes the vault shreds its key material permanently; the value cannot be recovered.\n\n" +
                       "To keep it, open the secret and rotate it with a renewal period. Rotating without one replaces the " +
                       "value but leaves the deadline in place, so the new value would still be shredded on schedule.\n\n" +
                       "— EclipsVault";

            return new Draft(user.Email, $"'{secretName}' expires in {days} day(s)", body, "SecretExpiring");
        }, ct);

    public async Task<IReadOnlyList<EmailLogDto>> ListRecentAsync(int max, CancellationToken ct)
        => (await _log.ListRecentAsync(max, ct)).Select(Map).ToList();

    /// <summary>
    /// Composes, delivers, and records one notification. Returns true once the outbox row is
    /// written — the outbox, not the SMTP result, is the record of the notice, so a Failed send is
    /// still "recorded" and the caller must not retry it into a loop.
    /// </summary>
    private async Task<bool> SafeSendAsync(Func<Task<Draft?>> compose, CancellationToken ct)
    {
        try
        {
            var draft = await compose();
            if (draft is null || string.IsNullOrWhiteSpace(draft.To))
            {
                return false;
            }

            var body = draft.Body.Length > MaxBodyLength ? draft.Body[..MaxBodyLength] : draft.Body;
            var status = EmailDeliveryStatus.Suppressed;
            string? error = null;

            if (_options.Enabled)
            {
                try
                {
                    await _sender.SendAsync(new EmailMessage(draft.To, draft.Subject, body), ct);
                    status = EmailDeliveryStatus.Sent;
                }
                catch
                {
                    status = EmailDeliveryStatus.Failed;
                    error = "Mail delivery failed.";
                }
            }

            await _log.AddAsync(new EmailLog
            {
                Id = Guid.NewGuid(),
                ToAddress = draft.To,
                Subject = draft.Subject,
                Body = body,
                EventType = draft.EventType,
                Transport = _sender.Transport,
                Status = status,
                Error = error,
                CreatedAtUtc = _clock.GetUtcNow()
            }, ct);

            return true;
        }
        catch
        {
            // Fail-soft: a notification failure must never propagate into the caller.
            return false;
        }
    }

    private static string DisplayName(User user)
        => string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;

    private static EmailLogDto Map(EmailLog e) => new(
        e.Id, e.ToAddress, e.Subject, e.Body, e.EventType, e.Transport, e.Status, e.Error, e.CreatedAtUtc);

    private sealed record Draft(string To, string Subject, string Body, string EventType);
}
