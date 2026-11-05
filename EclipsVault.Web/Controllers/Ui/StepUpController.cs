using System.Globalization;
using System.Security.Claims;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Middleware;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

[ApiController]
[Route("api/ui/step-up")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StepUpController(IStepUpService stepUp, TimeProvider clock) : ControllerBase
{
    [HttpPost]
    [ServiceFilter(typeof(AuthThrottleFilter))]
    [RequestSizeLimit(4096)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Verify([FromBody] StepUpRequest request, CancellationToken ct)
    {
        if (User.GetSessionStampOrNull() is not { } stamp)
        {
            return UiApiProblem.StaleSession();
        }

        try
        {
            if (!await stepUp.VerifyAsync(User.GetUserId(), stamp, request.Code ?? "", ct))
            {
                return AdministratorProblems.Problem(StatusCodes.Status400BadRequest, "step_up_failed",
                    "That authenticator code is not valid. Try again.");
            }
        }
        catch (StaleProofException)
        {
            return UiApiProblem.StaleSession();
        }

        // Keep the original generation, revocation version, session id and ticket lifetime. A proof
        // must never renew a concurrently retired session under newer credentials or store state.
        var authentication = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var claims = User.Claims.Where(claim => claim.Type != VaultClaimTypes.StepUpTime).ToList();
        claims.Add(new Claim(VaultClaimTypes.StepUpTime, clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), authentication.Properties);
        return NoContent();
    }
}
