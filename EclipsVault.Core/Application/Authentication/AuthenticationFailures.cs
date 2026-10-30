using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Authentication;

public sealed class AuthenticationFailures(
    IUserRepository users, IAuthenticationAttempt attempt, LockoutPolicy policy, TimeProvider clock)
{
    public async Task RecordAsync(User account, AuditAction action, string reason, CancellationToken ct)
    {
        var locked = await users.RecordFailedAttemptAsync(account, policy, clock.GetUtcNow(), ct);
        attempt.RecordUserEvent(action, account, reason);
        if (locked)
        {
            attempt.RecordUserEvent(AuditAction.AccountLockedOut, account,
                $"Locked until {account.LockedUntilUtc:u} after {policy.MaxFailedAttempts} failed attempts");
        }
    }

    public Task ResetAsync(User account, CancellationToken ct)
        => users.ClearFailedAttemptsAsync(account, ct);
}
