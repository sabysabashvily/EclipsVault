using EclipsVault.Core.Domain.Enums;
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
/// The Profile page's own account, from the session alone: its snapshot, the display name and avatar
/// it saves without account verification, and signing out everywhere, which is never metered.
/// </summary>
[ApiController]
[Route("api/ui/profile")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(ProfileProblemFilter))]
public sealed class ProfileDetailsController(
    IProfileService profiles,
    IPasskeyService passkeys,
    IMfaRecoveryService recovery,
    IAvatarProcessor avatars,
    ProfileOperationPreparation preparation,
    IProfileOperationRepository operations,
    AccountSignOut signOut,
    ITempDataDictionaryFactory tempData,
    IAntiforgery antiforgery) : ControllerBase
{
    private const int AvatarRequestLimit = 6 * 1024 * 1024;

    [HttpGet]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<ProfileResponse>> Get(CancellationToken ct)
    {
        if (await profiles.GetAsync(User.GetUserId(), ct) is not { } profile)
        {
            return StaleSession();
        }

        var security = await preparation.DescribeAsync(profile.Id, ct);
        var keys = await passkeys.ListForUserAsync(profile.Id, ct);
        var pending = User.GetProfileSessionOrNull() is { } session
            ? await operations.FindPendingEmailAsync(session.AccountId, session.SessionId, ct) : null;
        return new ProfileResponse(
            profile.Username,
            profile.DisplayName,
            profile.Email,
            profile.Clearance.ToDisplayName(),
            profile.ProjectKey,
            Url.Action("Avatar", "Profile", new { v = User.GetAvatarVersion() })!,
            profile.TotpEnabled,
            await recovery.CountRemainingAsync(profile.Id, ct),
            keys.Select(key => new ProfilePasskeyResponse(
                key.Id, key.Nickname, key.CreatedAtUtc.UtcDateTime, Action(security.PasskeyRemovals.GetValueOrDefault(key.Id)))).ToList(),
            security.Methods.Select(method => method.ToString()).ToList(),
            new ProfileChangesResponse(
                Action(security.Changes[ProfileOperationKind.ChangePassword]),
                Action(security.Changes[ProfileOperationKind.ResetAuthenticator]),
                Action(security.Changes[ProfileOperationKind.ReplaceRecoveryCodes]),
                Action(security.Changes[ProfileOperationKind.AddPasskey]),
                EmailCapability(security.Changes[ProfileOperationKind.ChangeEmail])),
            pending is null ? null : new PendingEmailResponse(pending.OperationId, pending.Address, pending.ExpiresAtUtc.UtcDateTime));
    }

    [HttpPatch("details")]
    [ProducesResponseType<ProfileDetailsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<ProfileDetailsResponse>> UpdateDetails([FromBody] ProfileDetailsRequest request, CancellationToken ct)
    {
        var displayName = await profiles.UpdateDisplayNameAsync(User.GetUserId(), request.DisplayName, ct);
        await SessionClaims.RenewReplacingAsync(HttpContext, (VaultClaimTypes.Display, displayName));
        return new ProfileDetailsResponse(displayName);
    }

    [HttpPost("avatar")]
    [RequestSizeLimit(AvatarRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = AvatarRequestLimit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> UploadAvatar(IFormFile? avatar, CancellationToken ct)
    {
        if (avatar is null || avatar.Length == 0)
        {
            return ProfileProblems.InvalidField("avatar", "Choose an image to upload.");
        }

        if (avatar.Length > avatars.MaxUploadBytes)
        {
            return ProfileProblems.InvalidField("avatar", $"Images must be {avatars.MaxUploadBytes / (1024 * 1024)} MB or smaller.");
        }

        using var image = new MemoryStream();
        await avatar.CopyToAsync(image, ct);
        await profiles.SetAvatarAsync(User.GetUserId(), image.ToArray(), ct);
        await SessionClaims.RenewReplacingAsync(HttpContext, (VaultClaimTypes.AvatarVersion, DateTimeOffset.UtcNow.Ticks.ToString()));
        return NoContent();
    }

    [HttpDelete("avatar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> RemoveAvatar(CancellationToken ct)
    {
        await profiles.RemoveAvatarAsync(User.GetUserId(), ct);
        await SessionClaims.RenewReplacingAsync(HttpContext, (VaultClaimTypes.AvatarVersion, DateTimeOffset.UtcNow.Ticks.ToString()));
        return NoContent();
    }

    /// <summary>
    /// Ends every session of the account, this one included, whatever the session store or audit trail
    /// answers. The outcome also waits on the next Legacy sign-in page, where the browser goes next.
    /// </summary>
    [HttpPost("sign-out-everywhere")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProfileProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> SignOutEverywhere()
    {
        SignOutEverywhereOutcome outcome;
        try
        {
            outcome = await signOut.EverywhereAsync(User.GetUserId(), User.GetUsername());
        }
        finally
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        var message = SignOutEverywhereMessages.For(outcome);
        var flash = tempData.GetTempData(HttpContext);
        flash[FlashExtensions.TypeKey] = outcome == SignOutEverywhereOutcome.SignedOut ? "info" : "error";
        flash[FlashExtensions.MessageKey] = message;
        return outcome switch
        {
            SignOutEverywhereOutcome.SignedOut => NoContent(),
            SignOutEverywhereOutcome.RevokedUnrecorded => ProfileProblems.Problem(StatusCodes.Status503ServiceUnavailable, "revoked_unrecorded", message),
            _ => ProfileProblems.Problem(StatusCodes.Status503ServiceUnavailable, "sign_out_incomplete", message),
        };
    }

    private static EmailChangeCapabilityResponse EmailCapability(ProfileRefusal? refusal)
        => new(refusal is null, ProfileProblems.CodeOf(refusal), refusal switch
        {
            null => null,
            ProfileRefusal.MailUnavailable => "Email changes require configured, enabled SMTP delivery.",
            _ => "Email changes require an existing local credential to verify your account.",
        });

    private static ProfileActionResponse Action(ProfileRefusal? refusal) => new(refusal is null, ProfileProblems.CodeOf(refusal));

    private ActionResult StaleSession()
    {
        UiAntiforgery.IssueToken(HttpContext, antiforgery);
        return UiApiProblem.StaleSession();
    }
}
