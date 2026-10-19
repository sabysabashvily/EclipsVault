using System.Globalization;
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
/// My activity: one page of the signed-in vault account's own actions, newest first. The account
/// comes from the session alone; the page number is the only thing the request chooses.
/// </summary>
[ApiController]
[Route("api/ui/activity")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
public sealed class ActivityController(IActivityService activity, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ActivityResponse>> Get([FromQuery] string? page, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId == Guid.Empty)
        {
            // No account id the vault issued: a stale session; send it to sign out.
            UiAntiforgery.IssueToken(HttpContext, antiforgery);
            return UiApiProblem.StaleSession();
        }

        // Bound as text so that, as on the Legacy page, a missing or non-integer page means page 1
        // rather than an automatic 400. Core turns zero and negative numbers into page 1 too.
        var requested = int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 1;
        var feed = await activity.GetForUserAsync(userId, requested, ActivityService.DefaultPageSize, ct);

        return new ActivityResponse(
            feed.Page,
            feed.PageSize,
            feed.HasMore,
            feed.HasPrevious,
            feed.NotableCount,
            feed.Items.Select(item => new ActivityItemResponse(
                item.TimestampUtc.UtcDateTime,
                item.Category.ToString(),
                item.Title,
                item.Severity.ToString(),
                item.ResourceName,
                item.SourceIp)).ToList());
    }
}
