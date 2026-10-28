using System.Text;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

/// <summary>
/// Self-service account management for the signed-in user: profile details, avatar,
/// password, personal MFA, and session control. Anything here acts only on the
/// caller's own account.
/// </summary>
public sealed class ProfileController : VaultController
{
    /// <summary>Session key holding the challenge issued for an in-flight passkey registration.</summary>
    private const string PasskeyRegistrationKey = "passkey:registration";

    /// <summary>TempData key that carries a freshly generated code set across the post-redirect-get to the one-time display page.</summary>
    private const string RecoveryCodesTempDataKey = "MfaRecoveryCodes";

    private readonly IProfileService _profile;
    private readonly IAvatarProcessor _avatarProcessor;
    private readonly IPasskeyService _passkeys;
    private readonly IPasskeyCeremonyStore _passkeyCeremonies;
    private readonly IMfaRecoveryService _recovery;
    private readonly IBreachedPasswordScreen _breachScreen;
    private readonly SessionIssuer _issuer;
    private readonly AccountSignOut _signOut;

    public ProfileController(
        IProfileService profile,
        IAvatarProcessor avatarProcessor,
        IPasskeyService passkeys,
        IPasskeyCeremonyStore passkeyCeremonies,
        IMfaRecoveryService recovery,
        IBreachedPasswordScreen breachScreen,
        SessionIssuer issuer,
        AccountSignOut signOut)
    {
        _profile = profile;
        _avatarProcessor = avatarProcessor;
        _passkeys = passkeys;
        _passkeyCeremonies = passkeyCeremonies;
        _recovery = recovery;
        _breachScreen = breachScreen;
        _issuer = issuer;
        _signOut = signOut;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var profile = await _profile.GetAsync(CurrentUserId(), ct);
        if (profile is null)
        {
            return RedirectToAction("Logout", "Account");
        }

        var model = ProfileViewModel.From(profile);
        model.Passkeys = await _passkeys.ListForUserAsync(CurrentUserId(), ct);
        model.RecoveryCodesRemaining = await _recovery.CountRemainingAsync(CurrentUserId(), ct);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Update(ProfileViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var current = await _profile.GetAsync(CurrentUserId(), ct);
            model.HasCustomAvatar = current?.HasCustomAvatar ?? false;
            model.Username = current?.Username ?? string.Empty;
            return View(nameof(Index), model);
        }

        ProfileUpdate update;
        try
        {
            update = await _profile.UpdateAsync(CurrentUserId(), CurrentCredentialGeneration(), model.DisplayName, model.Email, ct);
        }
        catch (Exception ex) when (ex is ProfileException or EmailAddressTakenException or StaleProofException)
        {
            this.FlashError(ex.Message);
            return RedirectToAction(nameof(Index));
        }

        if (!update.EmailChanged)
        {
            await SessionClaims.RenewReplacingAsync(HttpContext, (VaultClaimTypes.Display, update.Profile.DisplayName));
            this.FlashSuccess("Your profile was updated.");
            return RedirectToAction(nameof(Index));
        }

        var display = (VaultClaimTypes.Display, update.Profile.DisplayName);
        return await ReplaceSessionAsync(update.CredentialGeneration, "Your profile was updated. Any other signed-in devices have been signed out.", display);
    }

    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar(IFormFile? avatar, CancellationToken ct)
    {
        if (avatar is null || avatar.Length == 0)
        {
            this.FlashError("Choose an image to upload.");
            return RedirectToAction(nameof(Index));
        }

        if (avatar.Length > _avatarProcessor.MaxUploadBytes)
        {
            this.FlashError($"Images must be {_avatarProcessor.MaxUploadBytes / (1024 * 1024)} MB or smaller.");
            return RedirectToAction(nameof(Index));
        }

        try
        {
            using var stream = new MemoryStream();
            await avatar.CopyToAsync(stream, ct);
            await _profile.SetAvatarAsync(CurrentUserId(), stream.ToArray(), ct);
            await SessionClaims.RenewReplacingAsync(HttpContext, (VaultClaimTypes.AvatarVersion, DateTimeOffset.UtcNow.Ticks.ToString()));
            this.FlashSuccess("Your profile picture was updated.");
        }
        catch (ProfileException ex)
        {
            this.FlashError(ex.Message);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> RemoveAvatar(CancellationToken ct)
    {
        await _profile.RemoveAvatarAsync(CurrentUserId(), ct);
        await SessionClaims.RenewReplacingAsync(HttpContext, (VaultClaimTypes.AvatarVersion, DateTimeOffset.UtcNow.Ticks.ToString()));
        this.FlashSuccess("Your profile picture was removed.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Serves a user's avatar as an image: the stored PNG if present, otherwise a
    /// generated initials SVG. Behind authentication like every other route.
    /// </summary>
    [HttpGet]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> Avatar(Guid? id, string? seed, CancellationToken ct)
    {
        var userId = id ?? CurrentUserId();
        var png = await _profile.GetAvatarPngAsync(userId, ct);
        if (png is not null)
        {
            return File(png, "image/png");
        }

        var svg = Identicon.Svg(string.IsNullOrWhiteSpace(seed) ? userId.ToString() : seed);
        return File(Encoding.UTF8.GetBytes(svg), "image/svg+xml");
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        long committed;
        try
        {
            committed = await _profile.ChangePasswordAsync(
                CurrentUserId(), CurrentCredentialGeneration(), model.CurrentPassword, model.NewPassword, ct);
        }
        catch (Exception ex) when (ex is ProfileException or StaleProofException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        return await ReplaceSessionAsync(committed, "Your password was changed. Any other signed-in devices have been signed out.");
    }

    /// <summary>
    /// Live breach check for the password fields: returns whether a candidate appears in
    /// the compromised-password corpus. The password is only screened in-memory — never
    /// logged, audited, or stored — so it is safe to call as the user types.
    /// </summary>
    [HttpPost]
    public IActionResult CheckPasswordBreached([FromBody] PasswordCheckRequest request)
        => Json(new
        {
            compromised = !string.IsNullOrEmpty(request.Password) && _breachScreen.IsCompromised(request.Password)
        });

    [HttpPost]
    public async Task<IActionResult> ResetMfa(CancellationToken ct)
    {
        await _profile.ResetOwnMfaAsync(CurrentUserId(), CurrentCredentialGeneration(), ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        this.FlashInfo("Your authenticator was reset. Sign in to set up a new one.");
        return RedirectToAction("Login", "Account");
    }

    /// <summary>
    /// Issues a fresh set of single-use recovery codes (invalidating any the user held) and
    /// hands them to the one-time display page via TempData — they are never shown again.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> GenerateRecoveryCodes(CancellationToken ct)
    {
        var set = await _recovery.GenerateAsync(CurrentUserId(), CurrentCredentialGeneration(), ct);
        if (!await _issuer.ReplaceAsync(HttpContext, set.CredentialGeneration))
        {
            // The display page needs a session, so the codes could not be shown: they stay unseen.
            return ChangedSignInRequired("New recovery codes were generated. Sign in and deliberately generate a new set to see codes.");
        }

        TempData[RecoveryCodesTempDataKey] = string.Join('\n', set.Codes);
        return RedirectToAction(nameof(RecoveryCodes));
    }

    [HttpGet]
    public IActionResult RecoveryCodes()
    {
        // Codes are display-once: without the TempData payload (direct visit or a refresh)
        // there is nothing to show, so send the user back rather than reveal a blank page.
        if (TempData[RecoveryCodesTempDataKey] is not string joined)
        {
            this.FlashInfo("Recovery codes are shown only once, right after you generate them.");
            return RedirectToAction(nameof(Index));
        }

        return View(new RecoveryCodesViewModel
        {
            Codes = joined.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        });
    }

    [HttpPost]
    public async Task<IActionResult> SignOutEverywhere()
    {
        SignOutEverywhereOutcome outcome;
        try
        {
            outcome = await _signOut.EverywhereAsync(CurrentUserId(), CurrentUsername());
        }
        finally
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        var message = SignOutEverywhereMessages.For(outcome);
        switch (outcome)
        {
            case SignOutEverywhereOutcome.SignedOut:
                this.FlashInfo(message);
                break;
            case SignOutEverywhereOutcome.RevokedUnrecorded:
                this.FlashError(message);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "revoked_unrecorded" });
            default:
                this.FlashError(message);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "sign_out_incomplete" });
        }

        return RedirectToAction("Login", "Account");
    }

    /// <summary>Issues WebAuthn creation options and stashes the challenge server-side for verification.</summary>
    [HttpPost]
    public async Task<IActionResult> PasskeyRegisterBegin(CancellationToken ct)
    {
        var options = await _passkeys.BeginRegistrationAsync(CurrentUserId(), ct);
        var ceremony = new PasskeyRegistrationState(options.Challenge, CurrentUserId(), User.GetSessionId()!.Value, User.GetSessionStampOrNull()!);
        HttpContext.Session.SetString(PasskeyRegistrationKey, "pending");
        await _passkeyCeremonies.ReplaceAsync(HttpContext.Session.Id, PasskeyCeremonyPurpose.Registration, JsonSerializer.Serialize(ceremony), ct);
        return Content(options.OptionsJson, "application/json");
    }

    /// <summary>Verifies the authenticator's attestation and stores the new passkey.</summary>
    [HttpPost]
    public async Task<IActionResult> PasskeyRegisterComplete([FromBody] PasskeyRegistrationCompletion request, CancellationToken ct)
    {
        var ceremony = PasskeyRegistrationState.Read(await _passkeyCeremonies.TakeAsync(HttpContext.Session.Id, PasskeyCeremonyPurpose.Registration, ct));
        // The ceremony is bound to the credential generation it began under, not the session's current one.
        if (ceremony is null || !ceremony.BelongsTo(User))
        {
            return Json(new { success = false, error = "Registration timed out. Try again." });
        }

        var result = await _passkeys.CompleteRegistrationAsync(
            CurrentUserId(), ceremony.Stamp.CredentialGeneration, ceremony.Challenge, request.Credential.GetRawText(), request.Nickname, ct);
        if (result.CredentialGeneration is not { } committed)
        {
            return Json(new { success = false, error = result.Error });
        }

        // The page reloads after success, landing on sign-in when this browser could not be kept signed in.
        if (await _issuer.ReplaceAsync(HttpContext, committed))
        {
            this.FlashSuccess("Passkey added. You can now sign in with it.");
        }
        else
        {
            return ChangedSignInRequired("Passkey added. Sign in again to continue.");
        }

        return Json(new { success = true, error = (string?)null });
    }

    [HttpPost]
    public async Task<IActionResult> RemovePasskey(Guid id, CancellationToken ct)
    {
        if (await _passkeys.RemoveAsync(CurrentUserId(), CurrentCredentialGeneration(), id, ct) is not { } committed)
        {
            this.FlashError("That passkey could not be found.");
            return RedirectToAction(nameof(Index));
        }

        return await ReplaceSessionAsync(committed, "Passkey removed. Any other signed-in devices have been signed out.");
    }

    /// <summary>
    /// Keeps this browser signed in after a credential change by replacing its session, and lands on
    /// sign-in when it cannot: the change has committed either way, and every earlier session is ended.
    /// </summary>
    private async Task<IActionResult> ReplaceSessionAsync(
        long committedGeneration, string changedMessage, params (string Type, string Value)[] updates)
    {
        if (!await _issuer.ReplaceAsync(HttpContext, committedGeneration))
        {
            return ChangedSignInRequired($"{changedMessage} Sign in again to continue.");
        }

        if (updates.Length > 0)
        {
            await SessionClaims.RenewReplacingAsync(HttpContext, updates);
        }

        this.FlashSuccess(changedMessage);
        return RedirectToAction(nameof(Index));
    }

    private ObjectResult ChangedSignInRequired(string message)
        => StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "changed_sign_in_required", message });
}
