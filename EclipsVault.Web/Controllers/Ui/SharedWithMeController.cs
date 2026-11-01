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
[Route("api/ui/shared-with-me")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SharedWithMeController(
    ISecretGrantService shares, IAuthorizationService authorization, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SharedWithMeResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<SharedWithMeResponse>> Get(CancellationToken ct)
    {
        var recipientId = User.GetUserId();
        if (recipientId == Guid.Empty)
        {
            UiAntiforgery.IssueToken(HttpContext, antiforgery);
            return UiApiProblem.StaleSession();
        }

        // A share lifts only the project rule. Each row goes through the Secrets list's own filter,
        // share included, so a share the recipient could not use right now names nothing here.
        var listable = await authorization.VisibleToAsync(User, await shares.ListSharedWithUserAsync(recipientId, ct));
        return new SharedWithMeResponse(listable.Select(share => new ReceivedShareResponse(
            share.SecretId,
            share.Name,
            share.ProjectKey,
            share.Environment.ToString(),
            (int)share.Sensitivity,
            share.Sensitivity.ToDisplayName(),
            share.GrantedBy,
            share.ExpiresAtUtc?.UtcDateTime)).ToList());
    }
}
