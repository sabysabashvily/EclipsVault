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
[Route("api/ui/shared-by-me")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SharedByMeController(ISecretGrantService shares, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SharedByMeResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<SharedByMeResponse>> Get(CancellationToken ct)
    {
        var issuerId = User.GetUserId();
        if (issuerId == Guid.Empty)
        {
            return StaleSession();
        }

        var issued = await shares.ListIssuedByAsync(issuerId, ct);
        return new SharedByMeResponse(issued.Select(share => new IssuedShareResponse(
            share.GrantId,
            share.SecretName,
            share.GranteeUsername,
            share.CreatedAtUtc.UtcDateTime,
            share.ExpiresAtUtc?.UtcDateTime)).ToList());
    }

    [HttpDelete("{shareId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ShareProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ShareProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Revoke(Guid shareId, CancellationToken ct)
    {
        var issuerId = User.GetUserId();
        if (issuerId == Guid.Empty)
        {
            return StaleSession();
        }

        if (!await shares.RevokeIssuedAsync(shareId, issuerId, ct))
        {
            return new JsonResult(new ShareProblemResponse
            {
                Status = StatusCodes.Status404NotFound,
                Code = "share_not_found",
                Title = "That grant no longer exists, or it wasn't yours to revoke."
            })
            {
                StatusCode = StatusCodes.Status404NotFound,
                ContentType = "application/problem+json"
            };
        }

        return NoContent();
    }

    private ActionResult StaleSession()
    {
        UiAntiforgery.IssueToken(HttpContext, antiforgery);
        return UiApiProblem.StaleSession();
    }
}
