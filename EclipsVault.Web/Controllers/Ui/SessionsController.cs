using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Middleware;
using EclipsVault.Web.Models.Ui;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

[ApiController]
[Route("api/ui/sessions")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SessionsController(ISessionRegistry sessions, IAuditSink audit, IAntiforgery antiforgery) : ControllerBase
{
    private const string UnrecordedTitle = "The device was signed out, but the vault couldn't record it in the audit trail. Tell your administrator.";

    [HttpGet]
    [ProducesResponseType<SessionsResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<SessionsResponse>> Get(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId == Guid.Empty)
        {
            return StaleSession();
        }

        var current = User.GetSessionId();
        var live = await sessions.ListAsync(userId, ct);
        return new SessionsResponse(live.Select(session => new SessionResponse(
            session.SessionId,
            session.Device,
            session.IpAddress,
            session.CreatedAtUtc.UtcDateTime,
            session.LastSeenAtUtc.UtcDateTime,
            session.SessionId == current)).ToList());
    }

    [HttpDelete("{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<SessionProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SessionProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<SessionProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Revoke(Guid sessionId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId == Guid.Empty)
        {
            return StaleSession();
        }

        if (User.GetSessionId() == sessionId)
        {
            return SessionProblem(StatusCodes.Status409Conflict, "current_session",
                "That's the device you're using now — use Sign out for this one.");
        }

        if (!await sessions.RevokeAsync(userId, sessionId, ct))
        {
            return SessionProblem(StatusCodes.Status404NotFound, "session_not_found", "That session no longer exists, or it wasn't yours to sign out.");
        }

        try
        {
            await AuditRevokeAsync(sessionId);
        }
        catch (AuditWriteFailedException)
        {
            // Revocation is outside the audit transaction; restoring access would let an intruder stay.
            return SessionProblem(StatusCodes.Status503ServiceUnavailable, "revoked_unrecorded", UnrecordedTitle);
        }

        return NoContent();
    }

    [HttpPost("sign-out-others")]
    [ProducesResponseType<SignOutOthersResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<SessionProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<SignOutOthersResponse>> RevokeOthers(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId == Guid.Empty)
        {
            return StaleSession();
        }

        var current = User.GetSessionId();
        var others = (await sessions.ListAsync(userId, ct)).Where(session => session.SessionId != current);
        var revoked = new List<Guid>();
        foreach (var session in others)
        {
            if (await sessions.RevokeAsync(userId, session.SessionId, ct))
            {
                revoked.Add(session.SessionId);
            }
        }

        // Every device has lost access before the first audit write can fail or wait on the store.
        var unrecorded = false;
        foreach (var sessionId in revoked)
        {
            try
            {
                await AuditRevokeAsync(sessionId);
            }
            catch (AuditWriteFailedException)
            {
                unrecorded = true;
            }
        }

        return unrecorded
            ? SessionProblem(StatusCodes.Status503ServiceUnavailable, "revoked_unrecorded", UnrecordedTitle, revoked.Count)
            : new SignOutOthersResponse(revoked.Count);
    }

    private ActionResult StaleSession()
    {
        UiAntiforgery.IssueToken(HttpContext, antiforgery);
        return UiApiProblem.StaleSession();
    }

    // A disconnected request cannot undo a revocation, so its audit must still finish.
    private Task AuditRevokeAsync(Guid sessionId)
        => audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.SessionRevokedByUser,
            ResourceType = "Session",
            ResourceId = sessionId,
            Details = $"Revoked session {sessionId:N}"
        }, CancellationToken.None);

    private static JsonResult SessionProblem(int status, string code, string title, int? signedOut = null)
        => new(new SessionProblemResponse { Status = status, Code = code, Title = title, SignedOut = signedOut })
        {
            StatusCode = status,
            ContentType = "application/problem+json"
        };
}
