using EclipsVault.Core.Domain.Enums;
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
/// Who is signed in, for the Angular UI's first call. Part of the UI API: it accepts the session
/// cookie and nothing else, so an API key can never drive it (ADR-0005). It also hands Angular a
/// fresh antiforgery request token for its unsafe requests (ADR-0004).
/// </summary>
[ApiController]
[Route("api/ui/me")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
public sealed class MeController(
    IAuthorizationService authorization,
    IAntiforgery antiforgery,
    LicenseNudgeState licenseNudge) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get()
    {
        // A session missing the vault attribute claims is stale: refuse it rather than show a
        // clearance or project the server never issued, as the Legacy UI's My access does.
        var project = User.GetProject();
        if (User.GetClearanceOrNull() is not { } clearance || project.Length == 0)
        {
            UiAntiforgery.IssueToken(HttpContext, antiforgery);
            return UiApiProblem.StaleSession();
        }

        var areas = new List<string>();
        foreach (var area in UiAreas.All)
        {
            if ((await authorization.AuthorizeAsync(User, HttpContext, area.Policy)).Succeeded)
            {
                areas.Add(area.Key);
            }
        }

        UiAntiforgery.IssueToken(HttpContext, antiforgery);

        return new MeResponse(
            User.GetUsername(),
            User.GetDisplayName(),
            clearance.ToDisplayName(),
            project,
            areas,
            Url.Action(nameof(AvatarController.Image), "Avatar", new { v = User.GetAvatarVersion() })!,
            areas.Contains(UiAreas.License) ? LicenseNoticeFor(licenseNudge) : null);
    }

    /// <summary>The Legacy UI's licence banner, word for word, or null when it would not show.</summary>
    private static LicenseNotice? LicenseNoticeFor(LicenseNudgeState nudge)
    {
        if (!nudge.ShowBanner)
        {
            return null;
        }

        return nudge.Status != LicenseStatus.Valid
            ? new LicenseNotice(
                "Running unlicensed.",
                $"{nudge.Message} This changes nothing about how the vault runs — it only affects this notice.")
            : new LicenseNotice(
                "Premium features beyond your tier.",
                $"In use but not covered by your license: {string.Join(", ", nudge.PremiumFeaturesBeyondTier)}.");
    }
}
