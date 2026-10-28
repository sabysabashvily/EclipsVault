using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Persistence for prepared Profile operations. Preparing, cancelling and challenge changes are saved
/// immediately; they change no credential and record no event. Consuming an operation is staged, so
/// it is saved atomically with the change it authorizes.
/// </summary>
public interface IProfileOperationRepository
{
    Task<Profile.PendingEmail?> FindPendingEmailAsync(Guid accountId, Guid sessionId, CancellationToken ct);

    /// <summary>The operation, tracked, when it belongs to this account and session.</summary>
    Task<ProfileOperation?> FindAsync(Guid operationId, Guid accountId, Guid sessionId, CancellationToken ct);

    /// <summary>
    /// Operations of the account's other sessions that can still be verified or committed at <paramref name="nowUtc"/>.
    /// </summary>
    Task<int> CountLiveAsync(Guid accountId, Guid excludedSessionId, DateTimeOffset nowUtc, CancellationToken ct);

    /// <summary>
    /// Saves <paramref name="operation"/> as its session's only operation, deleting the session's earlier
    /// one and the account's operations that can no longer be used.
    /// </summary>
    Task ReplaceForSessionAsync(ProfileOperation operation, DateTimeOffset nowUtc, CancellationToken ct);

    /// <returns>False when the operation was already gone.</returns>
    Task<bool> DeleteAsync(Guid operationId, Guid accountId, Guid sessionId, CancellationToken ct);

    /// <summary>Gives the operation <paramref name="challenge"/> as its one challenge in flight, replacing any other.</summary>
    /// <returns>False when the operation was already gone.</returns>
    Task<bool> IssueChallengeAsync(ProfileOperation operation, PasskeyCeremonyPurpose purpose, string challenge,
        DateTimeOffset expiresAtUtc, CancellationToken ct);

    /// <summary>
    /// Spends the challenge the operation was loaded with, immediately and only if it is still the one
    /// in flight, for this purpose, and unexpired at <paramref name="nowUtc"/>.
    /// </summary>
    /// <returns>True when this call spent it; false when it expired, was replaced or another request spent it.</returns>
    Task<bool> TryConsumeChallengeAsync(ProfileOperation operation, PasskeyCeremonyPurpose purpose, DateTimeOffset nowUtc,
        CancellationToken ct);

    /// <summary>
    /// Reloads the operation's stored values. Call it while holding its account's lock.
    /// </summary>
    /// <returns>False when the operation was deleted.</returns>
    Task<bool> RefreshAsync(ProfileOperation operation, CancellationToken ct);

    /// <summary>Stages deleting the operation, so the change it authorized consumes it.</summary>
    void StageConsumption(ProfileOperation operation);
}
