using System.Security.Claims;
using System.Text;
using EclipsVault.Core.Domain.Exceptions;
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
    private const string PasskeyRegistrationChallengeKey = "passkey:registration:challenge";

    /// <summary>TempData key that carries a freshly generated code set across the post-redirect-get to the one-time display page.</summary>
    private const string RecoveryCodesTempDataKey = "MfaRecoveryCodes";

    private readonly IProfileService _profile;
    private readonly IAvatarProcessor _avatarProcessor;
    private readonly ISessionRevocationService _revocation;
    private readonly IPasskeyService _passkeys;
    private readonly IMfaRecoveryService _recovery;
    private readonly IBreachedPasswordScreen _breachScreen;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(
        IProfileService profile,
        IAvatarProcessor avatarProcessor,
        ISessionRevocationService revocation,
        IPasskeyService passkeys,
        IMfaRecoveryService recovery,
        IBreachedPasswordScreen breachScreen,
        TimeProvider clock,
        ILogger<ProfileController> logger)
    {
        _profile = profile;
        _avatarProcessor = avatarProcessor;
        _revocation = revocation;
        _passkeys = passkeys;
        _recovery = recovery;
        _breachScreen = breachScreen;
        _clock = clock;
        _logger = logger;
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

        try
        {
            var updated = await _profile.UpdateAsync(CurrentUserId(), model.DisplayName, model.Email, ct);
            await ReissueReplacingClaimsAsync((VaultClaimTypes.Display, updated.DisplayName));
            this.FlashSuccess("Your profile was updated.");
        }
        catch (Exception ex) when (ex is ProfileException or EmailAddressTakenException)
        {
            this.FlashError(ex.Message);
        }

        return RedirectToAction(nameof(Index));
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
            await ReissueReplacingClaimsAsync((VaultClaimTypes.AvatarVersion, DateTimeOffset.UtcNow.Ticks.ToString()));
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
        await ReissueReplacingClaimsAsync((VaultClaimTypes.AvatarVersion, DateTimeOffset.UtcNow.Ticks.ToString()));
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

        try
        {
            await _profile.ChangePasswordAsync(CurrentUserId(), model.CurrentPassword, model.NewPassword, ct);
        }
        catch (ProfileException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        // The service revoked every session issued up to now — this one included. Re-issue THIS
        // device with a strong-auth time strictly after that instant (the kill switch compares at
        // one-second granularity, so an equal timestamp would revoke the session we are keeping), so
        // whoever just proved the new password stays signed in while every other device is signed
        // out on its next request.
        var freshAuthTime = _clock.GetUtcNow().AddSeconds(1).ToUnixTimeSeconds().ToString();
        await ReissueReplacingClaimsAsync((VaultClaimTypes.AuthTime, freshAuthTime));

        this.FlashSuccess("Your password was changed. Any other signed-in devices have been signed out.");
        return RedirectToAction(nameof(Index));
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
        await _profile.ResetOwnMfaAsync(CurrentUserId(), ct);
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
        var codes = await _recovery.GenerateAsync(CurrentUserId(), ct);
        TempData[RecoveryCodesTempDataKey] = string.Join('\n', codes);
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
    public async Task<IActionResult> SignOutEverywhere(CancellationToken ct)
    {
        // Revoke all sessions issued up to now (this device included), then sign out here.
        await _revocation.RevokeAsync(CurrentUserId(), _clock.GetUtcNow(), ct);
        _logger.LogInformation("User {Username} signed out of all sessions", User.Identity?.Name);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        this.FlashInfo("You have been signed out of all sessions. Sign in again to continue.");
        return RedirectToAction("Login", "Account");
    }

    /// <summary>Issues WebAuthn creation options and stashes the challenge server-side for verification.</summary>
    [HttpPost]
    public async Task<IActionResult> PasskeyRegisterBegin(CancellationToken ct)
    {
        var options = await _passkeys.BeginRegistrationAsync(CurrentUserId(), ct);
        HttpContext.Session.SetString(PasskeyRegistrationChallengeKey, options.Challenge);
        return Content(options.OptionsJson, "application/json");
    }

    /// <summary>Verifies the authenticator's attestation and stores the new passkey.</summary>
    [HttpPost]
    public async Task<IActionResult> PasskeyRegisterComplete([FromBody] PasskeyRegistrationCompletion request, CancellationToken ct)
    {
        var challenge = HttpContext.Session.GetString(PasskeyRegistrationChallengeKey);
        HttpContext.Session.Remove(PasskeyRegistrationChallengeKey);
        if (string.IsNullOrEmpty(challenge))
        {
            return Json(new { success = false, error = "Registration timed out. Try again." });
        }

        var result = await _passkeys.CompleteRegistrationAsync(
            CurrentUserId(), challenge, request.Credential.GetRawText(), request.Nickname, ct);

        if (result.Success)
        {
            this.FlashSuccess("Passkey added. You can now sign in with it.");
        }

        return Json(new { success = result.Success, error = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> RemovePasskey(Guid id, CancellationToken ct)
    {
        if (await _passkeys.RemoveAsync(CurrentUserId(), id, ct))
        {
            this.FlashSuccess("Passkey removed.");
        }
        else
        {
            this.FlashError("That passkey could not be found.");
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Re-issues the auth cookie with the given claim(s) replaced, preserving every claim not named
    /// in the update. Callers that leave the auth-time anchor alone (profile edits, avatar changes)
    /// do not disturb the session-revocation kill switch; the change-password flow deliberately
    /// replaces it, to survive the account-wide revocation it just raised.
    /// </summary>
    private async Task ReissueReplacingClaimsAsync(params (string Type, string Value)[] updates)
    {
        var replaced = updates.Select(u => u.Type).ToHashSet();
        var claims = User.Claims.Where(c => !replaced.Contains(c.Type)).ToList();
        claims.AddRange(updates.Select(u => new Claim(u.Type, u.Value)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false });
    }

}
