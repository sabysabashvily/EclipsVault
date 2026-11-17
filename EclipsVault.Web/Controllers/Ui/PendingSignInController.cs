using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Middleware;
using EclipsVault.Web.Models.Ui;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// The second factor of a pending sign-in: an authenticator code, a recovery code, or a first
/// authenticator enrollment. These are the UI API's only actions that accept the pending sign-in,
/// and they accept nothing else, a session included. Without a usable pending sign-in the answer is
/// <c>401 sign_in_expired</c> (ADR-0032).
/// </summary>
[ApiController]
[Route("api/ui/auth")]
[Authorize(AuthenticationSchemes = AuthSchemes.MfaPending)]
[ServiceFilter(typeof(AuthThrottleFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<SignInProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<SignInProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType<SignInProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
public sealed class PendingSignInController(
    IVaultAuthenticationService auth,
    IMfaRecoveryService recovery,
    SessionIssuer issuer,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpPost("totp")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<SignInRedirectResponse>> Totp([FromBody] AuthenticatorCodeRequest request, CancellationToken ct)
    {
        if (PendingSignIn() is not (var userId, var stamp))
        {
            return SignInExpired();
        }

        UserDto? user;
        try
        {
            user = await auth.VerifyTotpAsync(userId, stamp, request.Code, ct);
        }
        catch (StaleProofException)
        {
            return await SignInProblems.RestartAsync(HttpContext, antiforgery);
        }

        if (user is null)
        {
            return SignInProblems.Problem(StatusCodes.Status400BadRequest, "code_invalid", "Invalid authenticator code.");
        }

        if (!await FinishAsync(user, stamp, "Password + TOTP", ct))
        {
            return await SignInProblems.RestartAsync(HttpContext, antiforgery);
        }

        return new SignInRedirectResponse(SignInTarget.Choose(Url, request.ReturnUrl));
    }

    /// <summary>For an account that lost its authenticator: a single-use recovery code instead.</summary>
    [HttpPost("recovery-code")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<RecoveryCodeSignInResponse>> RecoveryCode([FromBody] RecoveryCodeSignInRequest request, CancellationToken ct)
    {
        if (PendingSignIn() is not (var userId, var stamp))
        {
            return SignInExpired();
        }

        UserDto? user;
        try
        {
            user = await auth.VerifyRecoveryCodeAsync(userId, stamp, request.Code, ct);
        }
        catch (StaleProofException)
        {
            return await SignInProblems.RestartAsync(HttpContext, antiforgery);
        }

        if (user is null)
        {
            return SignInProblems.Problem(StatusCodes.Status400BadRequest, "code_invalid", "That recovery code is not valid, or it has already been used.");
        }

        if (!await FinishAsync(user, stamp, "Password + recovery code", ct))
        {
            return await SignInProblems.RestartAsync(HttpContext, antiforgery);
        }

        return new RecoveryCodeSignInResponse(SignInTarget.Choose(Url, request.ReturnUrl), await recovery.CountRemainingAsync(user.Id, ct));
    }

    /// <summary>The authenticator a first sign-in enrolls. Writes no audit row.</summary>
    [HttpPost("enrollment")]
    public async Task<ActionResult<TotpEnrollmentResponse>> BeginEnrollment(CancellationToken ct)
    {
        if (PendingSignIn() is not (var userId, var stamp))
        {
            return SignInExpired();
        }

        TotpEnrollmentDto enrollment;
        try
        {
            enrollment = await auth.BeginTotpEnrollmentAsync(userId, stamp, ct);
        }
        catch (StaleProofException)
        {
            return await SignInProblems.RestartAsync(HttpContext, antiforgery);
        }

        return new TotpEnrollmentResponse(enrollment.SecretBase32, TotpQrCode.PngDataUri(enrollment.OtpAuthUri));
    }

    [HttpPost("enrollment/confirm")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<SignInRedirectResponse>> ConfirmEnrollment([FromBody] AuthenticatorCodeRequest request, CancellationToken ct)
    {
        if (PendingSignIn() is not (var userId, var stamp))
        {
            return SignInExpired();
        }

        try
        {
            var user = await auth.CompleteTotpEnrollmentAsync(userId, stamp, request.Code, ct);
            if (user is null)
            {
                return SignInProblems.Problem(StatusCodes.Status400BadRequest, "code_invalid", "The confirmation code did not match. Try again.");
            }

            if (!await FinishAsync(user, stamp, "Password + TOTP enrollment", ct))
            {
                return await SignInProblems.RestartAsync(HttpContext, antiforgery);
            }
        }
        catch (StaleProofException)
        {
            return await SignInProblems.RestartAsync(HttpContext, antiforgery);
        }

        return new SignInRedirectResponse(SignInTarget.Choose(Url, request.ReturnUrl));
    }

    // Issues the session, whose request token the next request needs.
    private async Task<bool> FinishAsync(UserDto user, SessionStamp stamp, string details, CancellationToken ct)
    {
        if (!await issuer.SignInAsync(HttpContext, user, stamp, new SignInAudit(AuditAction.LoginSucceeded, details), ct))
        {
            return false;
        }

        UiAntiforgery.IssueToken(HttpContext, antiforgery);
        return true;
    }

    // A pending sign-in without a stamp cannot be fenced, so it must start again.
    private (Guid UserId, SessionStamp Stamp)? PendingSignIn()
        => User.GetUserIdOrNull() is { } userId && User.GetSessionStampOrNull() is { } stamp ? (userId, stamp) : null;

    private JsonResult SignInExpired()
        => SignInProblems.Problem(StatusCodes.Status401Unauthorized, "sign_in_expired", UiApiProblem.SignInExpiredTitle);
}
