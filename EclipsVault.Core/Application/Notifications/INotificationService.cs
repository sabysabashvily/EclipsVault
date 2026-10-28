using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Notifications;

/// <summary>
/// Composes and dispatches notification emails for domain events, recording each to the
/// outbox. Every method is fail-soft — a notification never breaks the triggering operation.
/// </summary>
public interface INotificationService
{
    Task NotifyAccessRequestDecidedAsync(
        Guid requesterUserId, string secretName, bool approved, string reviewer, string? note, CancellationToken ct);

    Task NotifyPasswordChangedAsync(Guid userId, CancellationToken ct);

    Task NotifyCredentialChangedAsync(Guid userId, ProfileOperationKind kind, CancellationToken ct);

    Task NotifyUserProvisionedAsync(string email, string displayName, string username, CancellationToken ct);

    /// <summary>
    /// Warns a secret's owner that its TTL is nearly up and the lifecycle worker will shred it.
    /// Returns true only if the notice was actually composed and recorded, so the caller knows
    /// whether it may mark the notice as sent.
    /// </summary>
    Task<bool> NotifyExpiringSecretAsync(
        Guid ownerUserId, string secretName, DateTimeOffset expiresAtUtc, CancellationToken ct);

    /// <summary>The most recent outbox rows, newest first, for the admin Notifications page.</summary>
    Task<IReadOnlyList<EmailLogDto>> ListRecentAsync(int max, CancellationToken ct);
}
