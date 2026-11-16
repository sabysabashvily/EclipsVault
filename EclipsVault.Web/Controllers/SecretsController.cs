using System.Security.Claims;
using EclipsVault.Core.Application.Abac;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

/// <summary>
/// Thin HTTP layer over ISecretService. Every by-id action runs the ABAC policy
/// against the resource's attributes before anything sensitive happens; the service
/// layer independently enforces honey-token traps and fail-closed auditing.
/// </summary>
public sealed class SecretsController : VaultController
{
    private const string NotPermittedReason = "Only an administrator or a member of this secret's project can change it.";

    private readonly ISecretService _secrets;
    private readonly ISecretGrantService _grants;
    private readonly IAuthorizationService _authorization;
    private readonly IStepUpService _stepUp;
    private readonly ILicenseState _license;
    private readonly IEvaluationWindowStore _evaluations;
    private readonly TimeProvider _clock;
    private readonly ILogger<SecretsController> _logger;

    public SecretsController(
        ISecretService secrets,
        ISecretGrantService grants,
        IAuthorizationService authorization,
        IStepUpService stepUp,
        ILicenseState license,
        IEvaluationWindowStore evaluations,
        TimeProvider clock,
        ILogger<SecretsController> logger)
    {
        _secrets = secrets;
        _grants = grants;
        _authorization = authorization;
        _stepUp = stepUp;
        _license = license;
        _evaluations = evaluations;
        _clock = clock;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var details = await _secrets.GetDetailsAsync(id, ct);
        if (await CheckAccessAsync(details, ct) is { } denied)
        {
            return denied;
        }

        await _secrets.RecordViewedAsync(details, ct);
        return View(await BuildViewModelAsync(details, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Reveal(Guid id, CancellationToken ct)
    {
        var details = await _secrets.GetDetailsAsync(id, ct);
        if (await CheckAccessAsync(details, ct) is { } denied)
        {
            return denied;
        }

        if (StepUpNeeded(details.Sensitivity))
        {
            return View("Details", await BuildViewModelAsync(details, ct, stepUpRequired: true));
        }

        var revealed = await _secrets.RevealAsync(id, ct);
        return View("Details", await BuildViewModelAsync(details, ct, revealed.Value, "current value"));
    }

    /// <summary>
    /// Completes a reveal that required step-up: verifies a fresh authenticator code, refreshes
    /// the strong-auth clock, then decrypts. Handles both the current value and an archived version.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> StepUpReveal(Guid id, string? code, Guid? versionId, CancellationToken ct)
    {
        var details = await _secrets.GetDetailsAsync(id, ct);
        if (await CheckAccessAsync(details, ct) is { } denied)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(code) || !await _stepUp.VerifyAsync(CurrentUserId(), User.GetSessionStampOrNull()!, code, ct))
        {
            return View("Details", await BuildViewModelAsync(details, ct,
                stepUpRequired: true, stepUpVersionId: versionId,
                stepUpError: "That authenticator code is not valid. Try again."));
        }

        await StampStepUpAsync();

        if (versionId is { } vid)
        {
            var revealedVersion = await _secrets.RevealVersionAsync(id, vid, ct);
            var versions = await _secrets.ListVersionsAsync(id, ct);
            var number = versions.FirstOrDefault(v => v.Id == vid)?.VersionNumber;
            return View("Details", await BuildViewModelAsync(details, ct, revealedVersion.Value, number is int n ? $"version {n}" : "archived version"));
        }

        var revealed = await _secrets.RevealAsync(id, ct);
        return View("Details", await BuildViewModelAsync(details, ct, revealed.Value, "current value"));
    }

    [HttpPost]
    public async Task<IActionResult> Rotate(RotateSecretViewModel model, CancellationToken ct)
    {
        var details = await _secrets.GetDetailsAsync(model.Id, ct);
        if (await CheckAccessAsync(details, ct) is { } denied)
        {
            return denied;
        }

        if (!ModelState.IsValid)
        {
            this.FlashError("Enter the new value to rotate this secret.");
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        await _secrets.RotateAsync(
            model.Id,
            model.NewValue,
            string.IsNullOrWhiteSpace(model.ChangeNote) ? null : model.ChangeNote.Trim(),
            model.RenewTtlDays,
            ct);

        this.FlashSuccess(model.RenewTtlDays is > 0
            ? $"Secret rotated and renewed for {model.RenewTtlDays} more day(s). The previous value was archived to version history."
            : "Secret rotated. The previous value was archived to version history.");
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    /// <summary>
    /// Rotates a managed secret: the vault changes the real principal's password upstream and stores
    /// the result. No new value is submitted — the whole point is that a human never handles it.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RotateManaged(Guid id, int? renewTtlDays, CancellationToken ct)
    {
        var details = await _secrets.GetDetailsAsync(id, ct);
        if (await CheckAccessAsync(details, ct) is { } denied)
        {
            return denied;
        }

        try
        {
            await _secrets.RotateManagedAsync(id, renewTtlDays, ct);
            this.FlashSuccess(
                "Rotated upstream. The vault generated a new password, changed the real credential, and stored it — " +
                "the previous value was archived to version history.");
        }
        catch (PremiumFeatureNotLicensedException)
        {
            this.FlashError("The 30-day evaluation period for managed rotation has ended. Rotating the real credential needs a licence — the stored value is unaffected.");
        }
        catch (VaultAdminException ex)
        {
            this.FlashError(ex.Message);
        }
        catch (UpstreamRotationRefusedException ex)
        {
            // The backend's own message stays in the log: it can name servers and logins.
            _logger.LogError(ex.InnerException, "The backend refused the upstream rotation of secret {SecretId}", id);
            this.FlashError(ex.Message);
        }
        catch (UpstreamRotationDriftedException ex)
        {
            if (!ex.Recorded)
            {
                _logger.LogCritical(ex, "Secret {SecretId} drifted from its login and the audit trail could not record it", id);
            }

            this.FlashError(ex.Message);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RevealVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        var details = await _secrets.GetDetailsAsync(id, ct);
        if (await CheckAccessAsync(details, ct) is { } denied)
        {
            return denied;
        }

        if (StepUpNeeded(details.Sensitivity))
        {
            return View("Details", await BuildViewModelAsync(details, ct, stepUpRequired: true, stepUpVersionId: versionId));
        }

        var revealed = await _secrets.RevealVersionAsync(id, versionId, ct);
        var versions = await _secrets.ListVersionsAsync(id, ct);
        var number = versions.FirstOrDefault(v => v.Id == versionId)?.VersionNumber;
        var label = number is int n ? $"version {n}" : "archived version";
        return View("Details", await BuildViewModelAsync(details, ct, revealed.Value, label));
    }

    [HttpPost]
    public async Task<IActionResult> RestoreVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        var details = await _secrets.GetDetailsAsync(id, ct);
        if (await CheckAccessAsync(details, ct) is { } denied)
        {
            return denied;
        }

        await _secrets.RestoreVersionAsync(id, versionId, ct);
        this.FlashSuccess("Secret reverted to the selected version. The value it replaced was archived.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Grant(ShareSecretViewModel model, CancellationToken ct)
    {
        var details = await _secrets.GetDetailsAsync(model.SecretId, ct);
        if (await CheckAccessAsync(details, ct) is { } denied)
        {
            return denied;
        }

        if (!CanShare(details))
        {
            await _secrets.RecordAccessDeniedAsync(details, [NotPermittedReason], ct);
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            this.FlashError("Enter the username or email of the person to share with.");
            return RedirectToAction(nameof(Details), new { id = model.SecretId });
        }

        try
        {
            await _grants.GrantAsync(model.SecretId, model.GranteeUsernameOrEmail, model.TtlDays, ct);
            this.FlashSuccess($"Access to '{details.Name}' was shared with {model.GranteeUsernameOrEmail}.");
        }
        catch (SharingException ex)
        {
            this.FlashError(ex.Message);
        }

        return RedirectToAction(nameof(Details), new { id = model.SecretId });
    }

    [HttpPost]
    public async Task<IActionResult> RevokeGrant(Guid id, Guid grantId, CancellationToken ct)
    {
        var details = await _secrets.GetDetailsAsync(id, ct);
        if (await CheckAccessAsync(details, ct) is { } denied)
        {
            return denied;
        }

        if (!CanShare(details))
        {
            await _secrets.RecordAccessDeniedAsync(details, [NotPermittedReason], ct);
            return Forbid();
        }

        if (await _grants.RevokeAsync(grantId, id, ct))
        {
            this.FlashSuccess("Access revoked.");
        }
        else
        {
            this.FlashError("That grant no longer exists.");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var details = await _secrets.GetDetailsAsync(id, ct);
        if (await CheckAccessAsync(details, ct) is { } denied)
        {
            return denied;
        }

        await _secrets.DeleteAsync(id, ct);
        // The list is the Angular UI's, which reads the outcome from the address, not from TempData.
        return LocalRedirect("/Secrets?deleted=1");
    }

    /// <summary>Null when access is allowed; otherwise the refusal is recorded and the caller is sent to
    /// the Denied page with the policy's reasons, so they can see what was not satisfied.</summary>
    private async Task<IActionResult?> CheckAccessAsync(SecretDetailsDto details, CancellationToken ct)
    {
        var result = await _authorization.AuthorizeAsync(User, details, VaultPolicies.SecretAccess);
        if (result.Succeeded)
        {
            return null;
        }

        var reasons = result.Failure?.FailureReasons.Select(r => r.Message).ToList() ?? [];
        await _secrets.RecordAccessDeniedAsync(details, reasons, ct);
        TempData[AccountController.DenialReasonsTempDataKey] = string.Join('\n', reasons);
        // The secret id rides the query string (reliable across the redirect) so the Denied
        // page can offer to file an access request for it.
        return RedirectToAction(nameof(AccountController.Denied), "Account", new { secretId = details.Id });
    }

    private async Task<SecretDetailsViewModel> BuildViewModelAsync(
        SecretDetailsDto dto, CancellationToken ct, string? revealedValue = null, string? revealedLabel = null,
        bool stepUpRequired = false, string? stepUpError = null, Guid? stepUpVersionId = null)
    {
        var canShare = CanShare(dto);
        var rotationLicensed = _license.Allows(LicenseFeatures.ManagedRotation);

        // Only ask when the answer can be shown — an unlicensed vault, on a managed secret. Otherwise
        // every details page would carry a licensing query it never renders.
        var rotationEvaluationDays = !rotationLicensed && dto.IsManaged
            ? await _evaluations.DaysRemainingAsync(LicenseFeatures.ManagedRotation, _clock, ct)
            : null;

        return new()
        {
            Id = dto.Id,
            Name = dto.Name,
            ProjectKey = dto.ProjectKey,
            Environment = dto.Environment,
            Sensitivity = dto.Sensitivity,
            Algorithm = dto.Algorithm,
            CreatedAtUtc = dto.CreatedAtUtc,
            UpdatedAtUtc = dto.UpdatedAtUtc,
            ExpiresAtUtc = dto.ExpiresAtUtc,
            IsManaged = dto.IsManaged,
            RotationPrincipal = dto.RotationPrincipal,
            RevealedValue = revealedValue,
            RevealedLabel = revealedLabel,
            Versions = await _secrets.ListVersionsAsync(dto.Id, ct),
            CanShare = canShare,
            Grants = canShare ? await _grants.ListForSecretAsync(dto.Id, ct) : [],
            StepUpRequired = stepUpRequired,
            StepUpError = stepUpError,
            StepUpVersionId = stepUpVersionId,
            StepUpMaxAgeMinutes = _stepUp.MaxAuthAgeMinutes,
            ManagedRotationLicensed = rotationLicensed,
            ManagedRotationEvaluationDaysRemaining = rotationEvaluationDays
        };
    }

    /// <summary>Whether revealing a secret of this sensitivity needs a fresh re-authentication right now.</summary>
    private bool StepUpNeeded(SensitivityLevel sensitivity)
        => _stepUp.IsRequired(sensitivity, User.GetLastStrongAuthUtc(), _clock.GetUtcNow());

    /// <summary>
    /// Re-issues the auth cookie with a fresh step-up timestamp, preserving every other claim, so the
    /// session keeps the stamp it was issued under.
    /// </summary>
    private async Task StampStepUpAsync()
    {
        var now = _clock.GetUtcNow().ToUnixTimeSeconds().ToString();
        var claims = User.Claims.Where(c => c.Type != VaultClaimTypes.StepUpTime).ToList();
        claims.Add(new Claim(VaultClaimTypes.StepUpTime, now));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false });
    }

    /// <summary>Sharing is managed by administrators and by members of the secret's own project.</summary>
    private bool CanShare(SecretDetailsDto dto)
        => User.IsAdmin() || string.Equals(User.GetProject(), dto.ProjectKey, StringComparison.OrdinalIgnoreCase);

}
