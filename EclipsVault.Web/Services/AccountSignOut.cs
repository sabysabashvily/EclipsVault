using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Web.Services;

/// <summary>
/// Signs an account out of every session: revoke first, record second (ADR-0021). A revocation is
/// never held back by the audit trail, because auditing being down is when an intruder benefits most.
/// </summary>
public sealed class AccountSignOut(ISessionRevocationService revocation, IAuditSink audit, ILogger<AccountSignOut> logger)
{
    // A disconnected request cannot undo a revocation, so neither step listens for cancellation.
    public async Task<SignOutEverywhereOutcome> EverywhereAsync(Guid userId, string username)
    {
        try
        {
            await revocation.RevokeAsync(userId, CancellationToken.None);
        }
        catch (Exception failure)
        {
            // A lost reply can hide a completed revocation, so this cannot claim either outcome.
            logger.LogError(failure, "Could not confirm signing out every session of account {UserId}", userId);
            return SignOutEverywhereOutcome.Incomplete;
        }

        try
        {
            await audit.WriteUserEventAsync(
                AuditAction.SessionsRevokedSelf, userId, username, "Signed out of every session by the account's owner", CancellationToken.None);
        }
        catch (AuditWriteFailedException)
        {
            return SignOutEverywhereOutcome.RevokedUnrecorded;
        }

        return SignOutEverywhereOutcome.SignedOut;
    }
}
