using EclipsVault.Web.Authentication;
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
[Route("api/ui/profile/email")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(ProfileProblemFilter))]
[RequestSizeLimit(4096)]
[ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
[ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
public sealed class ProfileEmailController(ProfileEmailChanges changes, SessionIssuer issuer, IAntiforgery antiforgery) : ControllerBase
{
    [HttpPost("request")]
    [ProducesResponseType<PendingEmailResponse>(StatusCodes.Status200OK, "application/json")]
    public new Task<ActionResult<PendingEmailResponse>> Request([FromBody] VerifiedOperationRequest request, CancellationToken ct)
        => SendAsync(request.OperationId, false, ct);

    [HttpPost("resend")]
    [ProducesResponseType<PendingEmailResponse>(StatusCodes.Status200OK, "application/json")]
    public Task<ActionResult<PendingEmailResponse>> Resend([FromBody] VerifiedOperationRequest request, CancellationToken ct)
        => SendAsync(request.OperationId, true, ct);

    [HttpPost("confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmEmailRequest request, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return UiApiProblem.StaleSession();
        }

        var generation = await changes.ConfirmAsync(session, request.OperationId, request.Code, ct);
        if (!await issuer.ReplaceAsync(HttpContext, generation))
        {
            return ProfileProblems.ChangedSignInRequired();
        }

        UiAntiforgery.IssueToken(HttpContext, antiforgery);
        return NoContent();
    }

    [HttpDelete("pending")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Cancel([FromBody] VerifiedOperationRequest request, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return UiApiProblem.StaleSession();
        }

        return await changes.CancelAsync(session, request.OperationId, ct)
            ? NoContent() : ProfileProblems.For(new(Core.Domain.Enums.ProfileRefusal.OperationNotFound));
    }

    private async Task<ActionResult<PendingEmailResponse>> SendAsync(Guid operationId, bool resend, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return UiApiProblem.StaleSession();
        }

        var pending = await changes.RequestAsync(session, operationId,
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", resend, ct);
        return new PendingEmailResponse(pending.OperationId, pending.Address, pending.ExpiresAtUtc.UtcDateTime);
    }
}
