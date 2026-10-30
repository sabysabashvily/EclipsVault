using System.Text.Json;
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
/// Prepares one Profile security change and verifies it with an existing credential (ADR-0022). An
/// operation id is only a correlation id: every action also needs the session that prepared it.
/// Preparing and every proof spend the shared per-address sign-in budget.
/// </summary>
[ApiController]
[Route("api/ui/profile/operations")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(ProfileProblemFilter))]
public sealed class ProfileOperationsController(
    ProfileOperationPreparation preparation,
    AccountVerification verification,
    IAntiforgery antiforgery) : ControllerBase
{
    /// <summary>Generous for a WebAuthn response, small enough to refuse before reading anything costly.</summary>
    private const int ProofRequestLimit = 64 * 1024;

    // Longer than any authenticator or recovery code, so a longer value is refused before it is hashed.
    private const int MaxCodeLength = 64;

    // The bootstrap administrator's password comes from configuration with no length rule, so this
    // only stops a value too long to be anyone's password from reaching Argon2id.
    private const int MaxCurrentPasswordLength = 1024;

    [HttpPost]
    [ServiceFilter(typeof(AuthThrottleFilter))]
    [RequestSizeLimit(ProofRequestLimit)]
    [ProducesResponseType<PreparedOperationResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<ActionResult<PreparedOperationResponse>> Prepare([FromBody] PrepareProfileOperationRequest request, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return StaleSession();
        }

        if (!TryParseName<ProfileOperationKind>(request.Kind, out var kind))
        {
            return ProfileProblems.InvalidField("kind", "Choose a change to make.");
        }

        var prepared = await preparation.PrepareAsync(session,
            new ProfileChangeRequest(kind, request.NewPassword, request.PasskeyNickname, request.PasskeyId, request.NewEmail), ct);
        return StatusCode(StatusCodes.Status201Created, new PreparedOperationResponse(
            prepared.OperationId,
            prepared.Kind.ToString(),
            prepared.ExpiresAtUtc.UtcDateTime,
            prepared.Methods.Select(method => method.ToString()).ToList()));
    }

    [HttpDelete("{operationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Cancel(Guid operationId, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return StaleSession();
        }

        return await preparation.CancelAsync(session, operationId, ct)
            ? NoContent()
            : ProfileProblems.For(new(ProfileRefusal.OperationNotFound));
    }

    [HttpPost("{operationId:guid}/verify")]
    [ServiceFilter(typeof(AuthThrottleFilter))]
    [RequestSizeLimit(ProofRequestLimit)]
    [ProducesResponseType<VerifiedOperationResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<ActionResult<VerifiedOperationResponse>> Verify(
        Guid operationId, [FromBody] VerifyProfileOperationRequest request, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return StaleSession();
        }

        if (!TryParseName<AccountProofMethod>(request.Method, out var method))
        {
            return ProfileProblems.InvalidField("method", "Choose how to verify your account.");
        }

        if (request.CurrentPassword is not { Length: > 0 and <= MaxCurrentPasswordLength })
        {
            return ProfileProblems.InvalidField("currentPassword", "Enter your current password.");
        }

        if (request.Code is not { Length: > 0 and <= MaxCodeLength })
        {
            return ProfileProblems.InvalidField("code", "Enter the code.");
        }

        var verified = await verification.VerifyWithCodeAsync(session, operationId, method, request.CurrentPassword, request.Code, ct);
        return new VerifiedOperationResponse(verified.OperationId, verified.CommitByUtc.UtcDateTime);
    }

    [HttpPost("{operationId:guid}/passkey/begin")]
    [ProducesResponseType<PasskeyOptionsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<PasskeyOptionsResponse>> BeginPasskey(Guid operationId, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return StaleSession();
        }

        var options = await verification.BeginPasskeyAsync(session, operationId, ct);
        using var document = JsonDocument.Parse(options);
        return new PasskeyOptionsResponse(document.RootElement.Clone());
    }

    [HttpPost("{operationId:guid}/passkey/complete")]
    [ServiceFilter(typeof(AuthThrottleFilter))]
    [RequestSizeLimit(ProofRequestLimit)]
    [ProducesResponseType<VerifiedOperationResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<ActionResult<VerifiedOperationResponse>> CompletePasskey(
        Guid operationId, [FromBody] PasskeyProofRequest request, CancellationToken ct)
    {
        if (User.GetProfileSessionOrNull() is not { } session)
        {
            return StaleSession();
        }

        if (request.Credential.ValueKind != JsonValueKind.Object)
        {
            return ProfileProblems.InvalidField("credential", "The passkey response could not be read.");
        }

        if (request.CurrentPassword?.Length > MaxCurrentPasswordLength)
        {
            return ProfileProblems.InvalidField("currentPassword", "Enter your current password.");
        }

        var verified = await verification.VerifyWithPasskeyAsync(
            session, operationId, request.Credential.GetRawText(), request.CurrentPassword, ct);
        return new VerifiedOperationResponse(verified.OperationId, verified.CommitByUtc.UtcDateTime);
    }

    /// <summary>Accepts an enum value by its exact name only, so a number cannot pick a value nobody named.</summary>
    private static bool TryParseName<TEnum>(string? name, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        return name is not null && Enum.GetNames<TEnum>().Contains(name, StringComparer.Ordinal)
            && Enum.TryParse(name, ignoreCase: false, out value);
    }

    private ActionResult StaleSession()
    {
        UiAntiforgery.IssueToken(HttpContext, antiforgery);
        return UiApiProblem.StaleSession();
    }
}
