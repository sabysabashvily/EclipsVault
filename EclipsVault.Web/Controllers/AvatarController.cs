using System.Text;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AvatarController(IProfileService profile) : VaultController
{
    // Legacy Users intentionally reads other accounts' avatars; writes use the session-only UI API.
    [HttpGet("/Profile/Avatar")]
    public async Task<IActionResult> Image(Guid? id, string? seed, CancellationToken ct)
    {
        var accountId = id ?? CurrentUserId();
        var png = await profile.GetAvatarPngAsync(accountId, ct);
        if (png is not null)
        {
            return File(png, "image/png");
        }

        var svg = Identicon.Svg(string.IsNullOrWhiteSpace(seed) ? accountId.ToString() : seed);
        return File(Encoding.UTF8.GetBytes(svg), "image/svg+xml");
    }
}
