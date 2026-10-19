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

/// <summary>
/// Sign-in history: the signed-in vault account's recent sign-in events and their summary. The
/// account comes from the session alone, and the request chooses nothing.
/// </summary>
[ApiController]
[Route("api/ui/sign-ins")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
public sealed class SignInsController(ISignInHistoryService history, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SignInsResponse>> Get(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId == Guid.Empty)
        {
            // No account id the vault issued: a stale session; send it to sign out.
            UiAntiforgery.IssueToken(HttpContext, antiforgery);
            return UiApiProblem.StaleSession();
        }

        var result = await history.GetForUserAsync(userId, ct);
        var summary = result.Summary;

        return new SignInsResponse(
            new SignInSummaryResponse(
                summary.SuccessCount,
                summary.FailedCount,
                summary.SuspiciousCount,
                summary.DistinctLocations,
                summary.LastSuccessUtc?.UtcDateTime,
                summary.LastFailedUtc?.UtcDateTime,
                summary.NeedsAttention),
            result.Events.Select(e => new SignInEventResponse(
                e.TimestampUtc.UtcDateTime,
                e.Outcome.ToString(),
                e.Method.ToString(),
                e.Title,
                e.SourceIp,
                e.LocationFlag.ToString())).ToList());
    }
}
