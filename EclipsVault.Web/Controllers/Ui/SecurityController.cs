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
/// Security checkup: the signed-in vault account's scored posture, most urgent control first. The
/// account comes from the session alone, and the request chooses nothing.
/// </summary>
[ApiController]
[Route("api/ui/security")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
public sealed class SecurityController(ISecurityCheckupService checkup, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SecurityResponse>> Get(CancellationToken ct)
    {
        var userId = User.GetUserId();
        // No account id the vault issued, or the account behind the session is gone: a stale
        // session either way; send it to sign out.
        if (userId == Guid.Empty || await checkup.GetForUserAsync(userId, ct) is not { } result)
        {
            UiAntiforgery.IssueToken(HttpContext, antiforgery);
            return UiApiProblem.StaleSession();
        }

        return new SecurityResponse(
            result.Score,
            result.Grade.ToString(),
            result.ActionNeededCount,
            result.RecommendedCount,
            result.PassCount,
            result.TopPriority?.Recommendation,
            result.Checks.Select(check => new SecurityCheckResponse(
                check.Kind.ToString(),
                check.Status.ToString(),
                check.Title,
                check.Detail,
                check.Recommendation,
                check.Fix.ToString())).ToList());
    }
}
