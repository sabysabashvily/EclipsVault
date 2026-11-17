using EclipsVault.Core.Application.Abac;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

/// <summary>
/// Dynamic secrets: credentials the vault mints on a real backend when you ask, and destroys when
/// the lease ends. Nothing here is stored — the value is shown once and is unrecoverable after.
///
/// Every role is gated by the same ABAC handler that guards stored secrets, so the list shows only
/// what the caller could actually issue, and issuing re-checks rather than trusting that filter.
/// </summary>
public sealed class DynamicSecretsController : VaultController
{
    private const string IssuedTempDataKey = "DynamicSecrets.Issued";

    private readonly IDynamicSecretService _dynamicSecrets;
    private readonly IAuthorizationService _authorization;
    private readonly ILicenseState _license;
    private readonly IEvaluationWindowStore _evaluations;
    private readonly IStepUpService _stepUp;
    private readonly TimeProvider _clock;
    private readonly ILogger<DynamicSecretsController> _logger;

    public DynamicSecretsController(
        IDynamicSecretService dynamicSecrets,
        IAuthorizationService authorization,
        ILicenseState license,
        IEvaluationWindowStore evaluations,
        IStepUpService stepUp,
        TimeProvider clock,
        ILogger<DynamicSecretsController> logger)
    {
        _dynamicSecrets = dynamicSecrets;
        _authorization = authorization;
        _license = license;
        _evaluations = evaluations;
        _stepUp = stepUp;
        _clock = clock;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await BuildAsync(null, ct));

    [HttpPost]
    public async Task<IActionResult> Issue(IssueCredentialViewModel form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            this.FlashError("Enter a lease length between 1 and 1440 minutes.");
            return RedirectToAction(nameof(Index));
        }

        var role = await _dynamicSecrets.FindRoleAsync(form.RoleId, ct);
        if (role is null)
        {
            this.FlashError("That role no longer exists.");
            return RedirectToAction(nameof(Index));
        }

        // Re-check on the way in: the list is filtered by the same policy, but a filtered list is a
        // convenience, never the enforcement point. Both checks come before the licence gate, so a
        // refused issue never starts the evaluation window.
        var access = await _authorization.AuthorizeAsync(User, role, VaultPolicies.SecretAccess);
        if (!access.Succeeded)
        {
            _logger.LogWarning(
                "User {Username} was denied a dynamic credential for role {RoleName}", CurrentUsername(), role.Name);
            await _dynamicSecrets.RecordAccessDeniedAsync(
                role, access.Failure?.FailureReasons.Select(reason => reason.Message) ?? [], ct);
            this.FlashError($"Your clearance and project do not permit issuing '{role.Name}'.");
            return RedirectToAction(nameof(Index));
        }

        if (_stepUp.IsRequired(role.Sensitivity, User.GetLastStrongAuthUtc(), _clock.GetUtcNow()))
        {
            this.FlashError($"Confirm it's you before issuing '{role.Name}': sign in again, then retry.");
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var issued = await _dynamicSecrets.IssueAsync(role.Id, form.TtlMinutes, ct);

            // Ride TempData across the redirect: the credential exists only in this response and is
            // never persisted, so it must survive exactly one render and no more.
            TempData[IssuedTempDataKey] = issued.LeaseId.ToString();
            return View(nameof(Index), await BuildAsync(issued, ct));
        }
        catch (PremiumFeatureNotLicensedException)
        {
            this.FlashError("The 30-day evaluation period for dynamic secrets has ended. Issuing new credentials needs a licence — existing leases are unaffected and can still be revoked.");
            return RedirectToAction(nameof(Index));
        }
        catch (VaultAdminException ex)
        {
            this.FlashError(ex.Message);
            return RedirectToAction(nameof(Index));
        }
        catch (MintRefusedException ex)
        {
            // The backend logged its own error by type and number; its message could quote the password.
            _logger.LogWarning("Minting a dynamic credential for role {RoleName} failed; nothing was issued", role.Name);
            this.FlashError(ex.Message);
            return RedirectToAction(nameof(Index));
        }
        catch (MintIncompleteException ex)
        {
            LogLoginMayRemain(ex.RoleName, ex.Login);
            this.FlashError(MintIncompleteException.FailureMessage);
            return RedirectToAction(nameof(Index));
        }
        catch (MintUnrecordedException ex)
        {
            LogLoginMayRemain(ex.RoleName, ex.Login);
            throw;
        }
    }

    [HttpPost]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        LeaseStatus? closed;
        try
        {
            closed = await _dynamicSecrets.RevokeAsync(id, CurrentUserId(), IsAdmin(), ct);
        }
        catch (RevokedUnrecordedException)
        {
            this.FlashError(RevokedUnrecordedException.FailureMessage);
            return RedirectToAction(nameof(Index));
        }

        switch (closed)
        {
            case LeaseStatus.Revoked:
                this.FlashSuccess("Credential handed back — it no longer works.");
                break;
            case LeaseStatus.RevocationFailed:
                var failed = await _dynamicSecrets.FindLeaseAsync(id, ct);
                this.FlashError(failed?.RevocationError ?? DynamicSecretService.BackendRefusedNote);
                break;
            default:
                // One message for unknown / closed / someone else's, matching the service's
                // indistinguishable "no".
                this.FlashError("That lease is not active, or is not yours to revoke.");
                break;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<DynamicSecretsViewModel> BuildAsync(IssuedCredentialDto? issued, CancellationToken ct)
    {
        var roles = await _dynamicSecrets.ListRolesAsync(ct);
        var permitted = new List<DynamicSecretRoleDto>(roles.Count);
        foreach (var role in roles)
        {
            if (role.IsEnabled && await CanIssueAsync(role))
            {
                permitted.Add(role);
            }
        }

        var isAdmin = IsAdmin();
        var licensed = _license.Allows(LicenseFeatures.DynamicSecrets);

        // Only ask when the answer can be shown; a licensed vault never renders the nudge.
        int? evaluationDays = null;
        var licenceNoteUnavailable = false;
        if (!licensed)
        {
            try
            {
                evaluationDays = await _evaluations.DaysRemainingAsync(LicenseFeatures.DynamicSecrets, _clock, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A licence lookup fails open: the page, and a credential just issued on it, still render.
                _logger.LogWarning(ex, "Reading the dynamic secrets evaluation window failed; the licence note is left out");
                licenceNoteUnavailable = true;
            }
        }

        return new DynamicSecretsViewModel
        {
            Roles = permitted,
            Leases = await _dynamicSecrets.ListLeasesAsync(CurrentUserId(), isAdmin, ct),
            ShowingEveryone = isAdmin,
            Issued = issued,
            Licensed = licensed,
            LicenceNoteUnavailable = licenceNoteUnavailable,
            EvaluationDaysRemaining = evaluationDays
        };
    }

    private void LogLoginMayRemain(string roleName, string login)
        => _logger.LogCritical(
            "A login minted for dynamic role {RoleName} may remain on the target: {Login}. Check the target and drop it (docs/INCIDENT_RESPONSE.md)",
            roleName, login);

    private async Task<bool> CanIssueAsync(IAbacResource role)
        => (await _authorization.AuthorizeAsync(User, role, VaultPolicies.SecretAccess)).Succeeded;

    private bool IsAdmin() => User.IsAdmin();
}
