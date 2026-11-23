using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EclipsVault.Web.Services;

/// <summary>
/// Signs an account out: this browser alone, or every session (ADR-0021). Both revoke first. A
/// revocation is never held back by the audit trail, because auditing being down is when an intruder
/// benefits most.
/// </summary>
public sealed class AccountSignOut(
    ISessionRevocationService revocation, ISessionRegistry sessions, IAuditSink audit, ILogger<AccountSignOut> logger)
{
    /// <summary>
    /// Revokes this browser's session, then clears its cookie. Clearing the cookie only makes this
    /// browser forget it; revoking the session id is the tombstone the cookie validator checks, so a
    /// copy taken earlier is refused on its next request. The cookie is cleared even when the
    /// tombstone cannot be stored: a live session left in the browser (a shared machine) is the
    /// likelier threat even when a copy may remain usable.
    /// </summary>
    /// <returns>False when the session could not be revoked, so a copy of the cookie stays valid until it expires.</returns>
    public async Task<bool> ThisSessionAsync(HttpContext context, CancellationToken ct)
    {
        var revoked = true;
        if (context.User.GetSessionId() is { } sessionId)
        {
            try
            {
                await sessions.RevokeAsync(context.User.GetUserId(), sessionId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sign-out could not revoke session {SessionId}; the cookie was cleared, but a copy of it stays valid until it expires", sessionId);
                revoked = false;
            }
        }

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return revoked;
    }

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
