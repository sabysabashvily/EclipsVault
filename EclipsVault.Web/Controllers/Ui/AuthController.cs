using System.Security.Claims;
using System.Text.Json;
using EclipsVault.Core.Application.Sso;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models.Ui;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// The sign-in calls a person makes before a session exists, account activation by invitation
/// (ADR-0033), and sign-out (ADR-0016, ADR-0032).
/// They are the UI API's only anonymous actions, each opened by name; the second-factor calls that
/// accept a pending sign-in live in <see cref="PendingSignInController"/>. Nothing here tells an
/// anonymous caller whether an account exists.
/// </summary>
[ApiController]
[Route("api/ui/auth")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(AuthThrottleFilter))]
[ForgetUnusablePendingSignIn]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(
    IVaultAuthenticationService auth,
    IAccountActivationService activation,
    IPasskeyService passkeys,
    IPasskeyCeremonyStore passkeyCeremonies,
    IIpBlacklist blacklist,
    IAuditSink audit,
    ISsoAvailability ssoAvailability,
    SessionIssuer issuer,
    AccountSignOut signOut,
    IAntiforgery antiforgery,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Session key holding the challenge issued for an in-flight passkey sign-in.</summary>
    private const string PasskeyAssertionKey = "passkey:assertion";

    private const string RecoveryFailed = "Recovery could not be completed with these credentials.";

    [AllowAnonymous]
    [HttpGet("options")]
    public ActionResult<AuthOptionsResponse> Options()
        => new AuthOptionsResponse(ssoAvailability.Enabled, ssoAvailability.DisplayName);

    [AllowAnonymous]
    [HttpPost("password")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesResponseType<PasswordStageResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<PasswordStageResponse>> Password([FromBody] PasswordSignInRequest request, CancellationToken ct)
    {
        var result = await auth.ValidateCredentialsAsync(request.Username, request.Password, ct);
        if (result.Status == CredentialStatus.Invalid || result.User is null || result.Stamp is null)
        {
            logger.LogWarning("Password stage failed for {Username} from {SourceIp}", request.Username, HttpContext.Connection.RemoteIpAddress);
            return SignInProblems.Problem(StatusCodes.Status400BadRequest, "sign_in_failed", "Invalid username or password.");
        }

        var pending = VaultClaimsFactory.CreatePendingMfaPrincipal(result.User, result.Stamp);
        await HttpContext.SignInAsync(AuthSchemes.MfaPending, pending);
        SignInProblems.IssueTokenFor(HttpContext, antiforgery, pending);
        return new PasswordStageResponse(result.Status == CredentialStatus.RequiresTotpEnrollment ? "enroll" : "totp");
    }

    /// <summary>
    /// Passwordless sign-in, stage one. It takes no username: only a discoverable passkey can answer,
    /// so the options are the same whoever asks (ADR-0032).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("passkey/begin")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<PasskeyOptionsResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<PasskeyOptionsResponse>> BeginPasskey([FromBody] PasskeySignInBeginRequest request, CancellationToken ct)
    {
        var options = await passkeys.BeginAssertionAsync(ct);
        // A session value makes ASP.NET issue its opaque browser-session cookie.
        HttpContext.Session.SetString(PasskeyAssertionKey, "pending");
        await passkeyCeremonies.ReplaceAsync(HttpContext.Session.Id, PasskeyCeremonyPurpose.SignIn,
            JsonSerializer.Serialize(new PasskeySignInState(options.Challenge, request.ReturnUrl)), ct);
        return new PasskeyOptionsResponse(JsonSerializer.Deserialize<JsonElement>(options.OptionsJson));
    }

    /// <summary>Passwordless sign-in, stage two: a verified passkey is both factors at once.</summary>
    [AllowAnonymous]
    [HttpPost("passkey/complete")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesResponseType<SignInRedirectResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<SignInRedirectResponse>> CompletePasskey([FromBody] PasskeySignInCompleteRequest request, CancellationToken ct)
    {
        var ceremony = PasskeySignInState.Read(await passkeyCeremonies.TakeAsync(HttpContext.Session.Id, PasskeyCeremonyPurpose.SignIn, ct));
        if (ceremony is null || string.IsNullOrEmpty(ceremony.Challenge))
        {
            return await SignInProblems.RestartAsync(HttpContext, antiforgery, SignInProblems.PasskeyTimedOut);
        }

        var result = await passkeys.CompleteAssertionAsync(ceremony.Challenge, request.Credential.GetRawText(), ct);
        if (!result.Success || result.User is null || result.Stamp is null)
        {
            logger.LogWarning("Passkey sign-in failed from {SourceIp}: {Error}", HttpContext.Connection.RemoteIpAddress, result.Error);
            return SignInProblems.Problem(StatusCodes.Status400BadRequest, "passkey_failed", result.Error ?? "Passkey sign-in failed.");
        }

        if (!await issuer.SignInAsync(HttpContext, result.User, result.Stamp, new SignInAudit(AuditAction.PasskeyLogin, "Passwordless sign-in with a passkey"), ct))
        {
            return await SignInProblems.RestartAsync(HttpContext, antiforgery, SignInProblems.PasskeyTimedOut);
        }

        UiAntiforgery.IssueToken(HttpContext, antiforgery);
        return new SignInRedirectResponse(SignInTarget.Choose(Url, ceremony.ReturnUrl));
    }

    /// <summary>
    /// Break-glass recovery for an administrator locked out by the intrusion defence: all factors at
    /// once, Top Secret only, audited, and lifting the block on their own range. The IP blacklist
    /// exempts this call and the Recover page, and nothing else (ADR-0032).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("recover")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    [ProducesResponseType<RecoverResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<RecoverResponse>> Recover([FromBody] RecoverRequest request, CancellationToken ct)
    {
        var source = HttpContext.Connection.RemoteIpAddress;
        var credentials = await auth.ValidateCredentialsAsync(request.Username, request.Password, ct);
        var stamp = credentials.Stamp;
        var user = credentials.Status == CredentialStatus.RequiresTotp && credentials.User is not null && stamp is not null
            ? await auth.VerifyBreakGlassAsync(credentials.User.Id, stamp, request.Code, ct)
            : null;
        if (user is null || stamp is null)
        {
            logger.LogWarning("Break-glass recovery failed for {Username} from {SourceIp}", request.Username, source);
            return SignInProblems.Problem(StatusCodes.Status400BadRequest, "recovery_failed", RecoveryFailed);
        }

        var previousSessionId = User.GetSessionId();
        try
        {
            var finished = await new BreakGlassSignIn(issuer, blacklist, audit).FinishAsync(HttpContext, user, stamp, source, ct);
            if (!finished.SessionIssued)
            {
                return SignInProblems.Problem(StatusCodes.Status400BadRequest, "recovery_failed", RecoveryFailed);
            }

            logger.LogWarning("Break-glass recovery completed by {Username} from {SourceIp}; block lifted: {BlockLifted}",
                user.Username, source, finished.BlockLifted);
            return new RecoverResponse(finished.BlockLifted);
        }
        finally
        {
            // A recorded session survives a later recovery failure; its next POST needs that identity's token too.
            if (User.GetSessionId() != previousSessionId)
            {
                UiAntiforgery.IssueToken(HttpContext, antiforgery);
            }
        }
    }

    /// <summary>
    /// The invited account and its authenticator seed, for the person holding the emailed token. The seed
    /// is generated on the first call only; nothing is spent (ADR-0033).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("activation")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ActivationResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<ActivationResponse>> InspectActivation([FromBody] ActivationRequest request, CancellationToken ct)
    {
        var invitation = await activation.InspectAsync(request.Token, ct);
        return invitation is null
            ? InvitationInvalid()
            : new ActivationResponse(invitation.Username, invitation.DisplayName, invitation.SecretBase32,
                TotpQrCode.PngDataUri(invitation.OtpAuthUri), invitation.ExpiresAtUtc);
    }

    /// <summary>
    /// Activates the invited account: the person's password under Profile's rules and the inspected
    /// authenticator, in one transaction with <c>AccountActivated</c>. A refused password or code keeps the invitation.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("activation/complete")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> CompleteActivation([FromBody] CompleteActivationRequest request, CancellationToken ct)
    {
        ActivationOutcome outcome;
        try
        {
            outcome = await activation.CompleteAsync(request.Token, request.Password, request.Code, ct);
        }
        catch (AccountInputException refused)
        {
            return ProfileProblems.InvalidField("password", refused.Message);
        }

        return outcome switch
        {
            ActivationOutcome.Activated => NoContent(),
            ActivationOutcome.CodeInvalid => ProfileProblems.Problem(StatusCodes.Status400BadRequest, "code_invalid",
                "The confirmation code did not match. Try again."),
            _ => InvitationInvalid(),
        };
    }

    private static JsonResult InvitationInvalid() => SignInProblems.Problem(StatusCodes.Status404NotFound, "invitation_invalid",
        "This invitation link is not valid. It may have expired or been used. Ask an administrator to send a new one.");

    /// <summary>
    /// Revokes this browser's session, then clears its cookie, as the Legacy sign-out does. Not
    /// throttled: it tests no credential and must work when a shared network has spent its budget.
    /// </summary>
    [HttpPost("sign-out")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<SignInProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> SignOutSession(CancellationToken ct)
    {
        var revoked = await signOut.ThisSessionAsync(HttpContext, ct);
        SignInProblems.IssueTokenFor(HttpContext, antiforgery, new ClaimsPrincipal(new ClaimsIdentity()));
        return revoked
            ? NoContent()
            : SignInProblems.Problem(StatusCodes.Status503ServiceUnavailable, "sign_out_incomplete",
                "This browser's cookie was cleared. Other devices may still be signed in.");
    }
}
