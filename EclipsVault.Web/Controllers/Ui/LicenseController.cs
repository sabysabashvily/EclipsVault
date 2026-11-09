using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Models.Ui;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

[ApiController]
[Route("api/ui/license")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.AdminOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LicenseController(ILicenseState license, LicenseNudgeState nudge, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<LicenseResponse>(StatusCodes.Status200OK, "application/json")]
    public ActionResult<LicenseResponse> Get()
    {
        var claims = license.Claims;
        return new LicenseResponse(license.Status.ToString(), license.Message,
            claims is null ? null : new LicenseDetailsResponse(claims.IssuedTo, claims.Tier.ToString(), claims.Contact,
                claims.LicenseId, claims.IssuedAtUtc, claims.NotAfterUtc, claims.UpdatesUntilUtc, claims.MaxNodes),
            claims?.UpdatesUntilUtc is { } until && until < clock.GetUtcNow(), nudge.PremiumFeaturesBeyondTier);
    }
}
