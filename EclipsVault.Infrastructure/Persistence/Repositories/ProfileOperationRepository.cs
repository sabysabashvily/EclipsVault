using System.Linq.Expressions;
using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class ProfileOperationRepository(EclipsVaultDbContext context, AccountTransaction transaction) : IProfileOperationRepository
{
    public Task<PendingEmail?> FindPendingEmailAsync(Guid accountId, Guid sessionId, CancellationToken ct)
        => context.ProfileOperations.Where(o => o.UserId == accountId && o.SessionId == sessionId
                && o.Kind == ProfileOperationKind.ChangeEmail && o.MailboxCodeHash != null)
            .Select(o => new PendingEmail(o.Id, o.TargetEmail!, o.MailboxExpiresAtUtc!.Value)).SingleOrDefaultAsync(ct);

    public Task<ProfileOperation?> FindAsync(Guid operationId, Guid accountId, Guid sessionId, CancellationToken ct)
        => context.ProfileOperations.FirstOrDefaultAsync(
            o => o.Id == operationId && o.UserId == accountId && o.SessionId == sessionId, ct);

    public Task<int> CountLiveAsync(Guid accountId, Guid excludedSessionId, DateTimeOffset nowUtc, CancellationToken ct)
        => context.ProfileOperations
            .Where(o => o.UserId == accountId && o.SessionId != excludedSessionId)
            .Where(IsLive(nowUtc))
            .CountAsync(ct);

    public async Task ReplaceForSessionAsync(ProfileOperation operation, DateTimeOffset nowUtc, CancellationToken ct)
    {
        await transaction.RunAsync(operation.UserId, async token =>
        {
            // Serialize admission across sessions: concurrent prepares share the same finite budget.
            if (await CountLiveAsync(operation.UserId, operation.SessionId, nowUtc, token) >= ProfileOperationLimits.LivePerAccount)
            {
                throw new ProfileOperationRefusedException(ProfileRefusal.Throttled);
            }

            await context.ProfileOperations
                .Where(o => o.UserId == operation.UserId && o.SessionId == operation.SessionId)
                .ExecuteDeleteAsync(token);
            await context.ProfileOperations
                .Where(o => o.UserId == operation.UserId)
                .Where(IsOutOfTime(nowUtc))
                .ExecuteDeleteAsync(token);
            if (operation.Kind == ProfileOperationKind.ChangeEmail)
            {
                await context.ProfileOperations.Where(o => o.UserId == operation.UserId && o.Kind == ProfileOperationKind.ChangeEmail)
                    .ExecuteDeleteAsync(token);
            }

            context.ProfileOperations.Add(operation);
            await context.SaveChangesAsync(token);
            return true;
        }, ct);
    }

    public async Task<bool> DeleteAsync(Guid operationId, Guid accountId, Guid sessionId, CancellationToken ct)
        => await transaction.RunAsync(accountId, async token => await context.ProfileOperations
            .Where(o => o.Id == operationId && o.UserId == accountId && o.SessionId == sessionId)
            .ExecuteDeleteAsync(token) == 1, ct);

    public async Task<bool> IssueChallengeAsync(ProfileOperation operation, PasskeyCeremonyPurpose purpose, string challenge,
        DateTimeOffset expiresAtUtc, CancellationToken ct)
        => await context.ProfileOperations
            .Where(o => o.Id == operation.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.Challenge, challenge)
                .SetProperty(o => o.ChallengePurpose, purpose)
                .SetProperty(o => o.ChallengeExpiresAtUtc, expiresAtUtc), ct) == 1;

    public async Task<bool> TryConsumeChallengeAsync(ProfileOperation operation, PasskeyCeremonyPurpose purpose, DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var challenge = operation.Challenge;
        return challenge is not null && await context.ProfileOperations
            .Where(o => o.Id == operation.Id && o.Challenge == challenge && o.ChallengePurpose == purpose
                        && o.ChallengeExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.Challenge, (string?)null)
                .SetProperty(o => o.ChallengePurpose, (PasskeyCeremonyPurpose?)null)
                .SetProperty(o => o.ChallengeExpiresAtUtc, (DateTimeOffset?)null), ct) == 1;
    }

    public async Task<bool> RefreshAsync(ProfileOperation operation, CancellationToken ct)
    {
        var entry = context.Entry(operation);
        await entry.ReloadAsync(ct);
        return entry.State != EntityState.Detached;
    }

    public void StageConsumption(ProfileOperation operation) => context.ProfileOperations.Remove(operation);

    /// <summary>Unverified and still within its verification window, or verified and still within its grant.</summary>
    private static Expression<Func<ProfileOperation, bool>> IsLive(DateTimeOffset nowUtc)
    {
        var grantedSince = nowUtc - ProfileOperationLimits.GrantLifetime;
        return o => (o.Kind == ProfileOperationKind.ChangeEmail && o.ExpiresAtUtc > nowUtc)
            || (o.VerifiedAtUtc == null && o.ExpiresAtUtc > nowUtc) || (o.VerifiedAtUtc != null && o.VerifiedAtUtc > grantedSince);
    }

    private static Expression<Func<ProfileOperation, bool>> IsOutOfTime(DateTimeOffset nowUtc)
    {
        var grantedSince = nowUtc - ProfileOperationLimits.GrantLifetime;
        return o => o.Kind == ProfileOperationKind.ChangeEmail
            ? o.ExpiresAtUtc <= nowUtc && (o.VerifiedAtUtc == null || o.VerifiedAtUtc <= grantedSince)
            : (o.VerifiedAtUtc == null && o.ExpiresAtUtc <= nowUtc) || (o.VerifiedAtUtc != null && o.VerifiedAtUtc <= grantedSince);
    }
}
