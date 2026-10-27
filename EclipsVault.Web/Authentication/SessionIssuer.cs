using System.Security.Claims;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EclipsVault.Web.Authentication;

/// <summary>
/// Issues interactive session cookies. A sign-in gets a session only while the revocation version its
/// proof captured is still the account's current one, so signing out everywhere during a sign-in ends
/// that sign-in too. After a credential change the caller's session is replaced by a new one under the
/// generation the change committed; the old session is never renewed.
/// </summary>
public sealed class SessionIssuer(IUserRepository users, ISessionRevocationService revocation, ISessionRegistry registry, ILogger<SessionIssuer> logger)
{
    /// <returns>False when the account was signed out everywhere after the proof began; then no session was issued.</returns>
    public async Task<bool> SignInAsync(HttpContext context, UserDto user, SessionStamp stamp)
    {
        if (!await HasCurrentGenerationAsync(user.Id, stamp.CredentialGeneration)
            || await revocation.FindAsync(user.Id) != stamp.RevocationVersion)
        {
            return false;
        }

        await context.SignOutAsync(AuthSchemes.MfaPending);
        var sessionId = Guid.NewGuid();
        await IssueAsync(context, VaultClaimsFactory.CreateSessionPrincipal(user, sessionId, DateTimeOffset.UtcNow, stamp));
        await RecordAsync(context, user.Id, sessionId, stamp);
        return true;
    }

    /// <summary>
    /// Replaces the caller's session after a credential change that committed
    /// <paramref name="committedGeneration"/>. The change stands either way, and the old session stays invalid.
    /// </summary>
    /// <returns>False when the caller must sign in again; then the current session was signed out instead.</returns>
    public async Task<bool> ReplaceAsync(HttpContext context, long committedGeneration)
    {
        var current = context.User;
        var userId = current.GetUserId();
        var stamp = current.GetSessionStampOrNull();
        if (stamp is null || !await TryRetireCurrentSessionForReplacementAsync(current, committedGeneration))
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return false;
        }

        var sessionId = Guid.NewGuid();
        var replacement = VaultClaimsFactory.CreateReplacementPrincipal(current, sessionId, committedGeneration);
        try
        {
            await IssueAsync(context, replacement);
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "Could not issue the replacement session of account {UserId}", userId);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return false;
        }
        // The rest of this request acts as the new session, so a later re-issue renews it, not the old one.
        context.User = replacement;
        await RecordAsync(context, userId, sessionId, stamp with { CredentialGeneration = committedGeneration });
        return true;
    }

    /// <summary>A sighting of the session <paramref name="sessionId"/> from this request, for the device list.</summary>
    public static SessionObservation ObservationOf(HttpContext context, Guid userId, Guid sessionId, SessionStamp stamp)
    {
        var now = DateTimeOffset.UtcNow;
        return new SessionObservation(
            userId,
            sessionId,
            stamp,
            UserAgentSummary.Describe(context.Request.Headers.UserAgent.ToString()),
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            now,
            now + SessionDefaults.InteractiveLifetime);
    }

    // The change has committed, so an unreachable session store must end in "sign in again",
    // not in an error page that hides whether the change happened.
    private async Task<bool> TryRetireCurrentSessionForReplacementAsync(ClaimsPrincipal current, long committedGeneration)
    {
        var userId = current.GetUserId();
        var stamp = current.GetSessionStampOrNull()!;
        try
        {
            return await HasCurrentGenerationAsync(userId, committedGeneration)
                && await revocation.FindAsync(userId) == stamp.RevocationVersion
                && current.GetSessionId() is { } sessionId
                && await registry.TryRetireForReplacementAsync(userId, sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not replace the session of user {UserId} after a credential change", userId);
            return false;
        }
    }

    private async Task<bool> HasCurrentGenerationAsync(Guid userId, long credentialGeneration)
    {
        var account = await users.FindByIdAsync(userId, CancellationToken.None);
        // The mutation may already have tracked this account; issuance must see a later commit too.
        return account is not null && await users.ReloadAsync(account, CancellationToken.None)
            && !account.IsDisabled && account.CredentialGeneration == credentialGeneration;
    }

    private static Task IssueAsync(HttpContext context, ClaimsPrincipal principal)
        => context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false });

    // The device list is best-effort metadata: failing to record a session must not undo issuing it.
    private async Task RecordAsync(HttpContext context, Guid userId, Guid sessionId, SessionStamp stamp)
    {
        try
        {
            await registry.RecordSeenAsync(ObservationOf(context, userId, sessionId, stamp));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not record the new session {SessionId} in the device list", sessionId);
        }
    }
}
