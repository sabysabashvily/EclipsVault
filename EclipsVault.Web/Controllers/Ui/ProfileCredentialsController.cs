using System.Text.Json;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Middleware;
using EclipsVault.Web.Models.Ui;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// The final action of each verified Profile change. A request names only the verified operation:
/// what changes was frozen when it was prepared. Every change except an authenticator reset keeps
/// this browser signed in on a brand-new session and ends every other session.
/// </summary>
[ApiController]
[Route("api/ui/profile")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(ProfileProblemFilter))]
public sealed class ProfileCredentialsController(
    VerifiedProfileChanges changes,
    SessionIssuer issuer,
    IAntiforgery antiforgery,
    ITempDataDictionaryFactory tempData) : ControllerBase
{
    private const int RegistrationRequestLimit = 64 * 1024;

    [HttpPost("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> ChangePassword([FromBody] VerifiedOperationRequest request, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return StaleSession();
        }

        var committed = await changes.ChangePasswordAsync(session, request.OperationId, ct);
        return await KeepSignedInAsync(committed) ?? NoContent();
    }

    /// <summary>Retires the authenticator and recovery codes and ends every session, this one included.</summary>
    [HttpPost("authenticator/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> ResetAuthenticator([FromBody] VerifiedOperationRequest request, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return StaleSession();
        }

        await changes.ResetAuthenticatorAsync(session, request.OperationId, ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // Only a completed reset may make sign-in show this one-shot security notice.
        var notice = tempData.GetTempData(HttpContext);
        notice[FlashExtensions.TypeKey] = "info";
        notice[FlashExtensions.MessageKey] = "Your authenticator was reset. Sign in to set up a new one.";
        return NoContent();
    }

    /// <summary>
    /// The only response that carries the new codes. Nothing keeps a copy: a lost response means
    /// generating a new set on purpose, never a retry.
    /// </summary>
    [HttpPost("recovery-codes")]
    [ProducesResponseType<RecoveryCodesResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<RecoveryCodesResponse>> ReplaceRecoveryCodes([FromBody] VerifiedOperationRequest request, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return StaleSession();
        }

        var set = await changes.ReplaceRecoveryCodesAsync(session, request.OperationId, ct);
        if (await KeepSignedInAsync(set.CredentialGeneration) is { } signInRequired)
        {
            return signInRequired;
        }

        return new RecoveryCodesResponse(set.Codes);
    }

    [HttpPost("passkeys/registration/begin")]
    [ProducesResponseType<PasskeyOptionsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<PasskeyOptionsResponse>> BeginPasskeyRegistration([FromBody] VerifiedOperationRequest request, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return StaleSession();
        }

        var options = await changes.BeginPasskeyRegistrationAsync(session, request.OperationId, ct);
        using var document = JsonDocument.Parse(options);
        return new PasskeyOptionsResponse(document.RootElement.Clone());
    }

    [HttpPost("passkeys/registration/complete")]
    [RequestSizeLimit(RegistrationRequestLimit)]
    [ProducesResponseType<RegisteredPasskeyResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<RegisteredPasskeyResponse>> CompletePasskeyRegistration(
        [FromBody] PasskeyRegistrationRequest request, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return StaleSession();
        }

        if (request.Credential.ValueKind != JsonValueKind.Object)
        {
            return ProfileProblems.InvalidField("credential", "The passkey response could not be read.");
        }

        var registered = await changes.CompletePasskeyRegistrationAsync(session, request.OperationId, request.Credential.GetRawText(), ct);
        if (await KeepSignedInAsync(registered.CredentialGeneration) is { } signInRequired)
        {
            return signInRequired;
        }

        var passkey = registered.Passkey;
        return new RegisteredPasskeyResponse(passkey.Id, passkey.Nickname, passkey.CreatedAtUtc.UtcDateTime);
    }

    [HttpDelete("passkeys/{passkeyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> RemovePasskey(Guid passkeyId, [FromBody] VerifiedOperationRequest request, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return StaleSession();
        }

        var committed = await changes.RemovePasskeyAsync(session, request.OperationId, passkeyId, ct);
        return await KeepSignedInAsync(committed) ?? NoContent();
    }

    /// <summary>
    /// Moves this browser to a new session under the committed generation, with a fresh antiforgery
    /// token. When it cannot, the change still stands, and the answer says so instead of an error.
    /// </summary>
    /// <returns>Null when the browser stayed signed in.</returns>
    private async Task<ActionResult?> KeepSignedInAsync(long committedGeneration)
    {
        if (!await issuer.ReplaceAsync(HttpContext, committedGeneration))
        {
            return ProfileProblems.ChangedSignInRequired();
        }

        UiAntiforgery.IssueToken(HttpContext, antiforgery);
        return null;
    }

    private ActionResult StaleSession()
    {
        UiAntiforgery.IssueToken(HttpContext, antiforgery);
        return UiApiProblem.StaleSession();
    }
}
