using EclipsVault.Core.Application.Abac;
using EclipsVault.Core.Application.DynamicSecrets;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Middleware;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// Dynamic secrets: credentials the vault mints on a real backend when asked and destroys when the lease
/// ends. The password is in the issue response only. Every role is gated by the handler that guards stored
/// secrets, so the list shows what the caller could issue now, and issuing checks again.
/// </summary>
[ApiController]
[Route("api/ui/dynamic-secrets")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DynamicSecretsController(
    IDynamicSecretService dynamicSecrets,
    IAuthorizationService authorization,
    ILicenseState license,
    IEvaluationWindowStore evaluations,
    IStepUpService stepUp,
    IUserRepository users,
    TimeProvider clock,
    ILogger<DynamicSecretsController> logger) : ControllerBase
{
    public const string NotLicensedMessage =
        "The 30-day evaluation period for dynamic secrets has ended. Issuing new credentials needs a licence — existing leases are unaffected and can still be revoked.";

    [HttpGet]
    [ProducesResponseType<DynamicSecretsResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<DynamicSecretsResponse>> Get(CancellationToken ct)
    {
        var roles = new List<DynamicRoleResponse>();
        foreach (var role in await dynamicSecrets.ListRolesAsync(ct))
        {
            if (role.IsEnabled && (await authorization.AuthorizeAsync(User, role, VaultPolicies.SecretAccess)).Succeeded)
            {
                roles.Add(new DynamicRoleResponse(role.Id, role.Name, role.Description, role.ProjectKey, role.Environment.ToString(),
                    (int)role.Sensitivity, role.Sensitivity.ToDisplayName(), role.Backend.ToString(), role.DefaultTtlMinutes, role.MaxTtlMinutes));
            }
        }

        var everyone = User.IsAdmin();
        var leases = (await dynamicSecrets.ListLeasesAsync(User.GetUserId(), everyone, ct))
            .Select(lease => new DynamicLeaseResponse(lease.Id, lease.RoleName, lease.CredentialIdentity, lease.Username,
                lease.IssuedAtUtc, lease.ExpiresAtUtc, lease.ClosedAtUtc, lease.Status.ToString(), FixedNote(lease.RevocationError)))
            .ToList();

        return new DynamicSecretsResponse(roles, everyone ? "everyone" : "own", leases, await LicenceAsync(ct));
    }

    [HttpPost("issue")]
    [ProducesResponseType<IssuedDynamicCredentialResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status402PaymentRequired, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Issue([FromBody] IssueDynamicCredentialRequest request, CancellationToken ct)
    {
        if (request.TtlMinutes is < 1 or > DynamicSecretService.LeaseCeilingMinutes)
        {
            const string message = "Enter a lease length between 1 and 1440 minutes.";
            return AdministratorProblems.Problem(StatusCodes.Status400BadRequest, "validation_failed", message,
                errors: new Dictionary<string, string[]> { ["ttlMinutes"] = [message] });
        }

        if (await dynamicSecrets.FindRoleAsync(request.RoleId, ct) is not { } role)
        {
            return AdministratorProblems.Problem(StatusCodes.Status404NotFound, "role_not_found", "That role no longer exists.");
        }

        // The list is filtered by the same policy, but a filtered list is a convenience, never the enforcement
        // point. Both checks come before the licence gate, so a refused issue never starts the evaluation window.
        var access = await authorization.AuthorizeAsync(User, role, VaultPolicies.SecretAccess);
        if (!access.Succeeded)
        {
            logger.LogWarning("User {Username} was denied a dynamic credential for role {RoleName}", User.Identity?.Name, role.Name);
            await dynamicSecrets.RecordAccessDeniedAsync(role, access.Failure?.FailureReasons.Select(reason => reason.Message) ?? [], ct);
            return AdministratorProblems.Problem(StatusCodes.Status403Forbidden, "access_denied",
                $"Your clearance and project do not permit issuing '{role.Name}'.");
        }

        if (stepUp.IsRequired(role.Sensitivity, User.GetLastStrongAuthUtc(), clock.GetUtcNow()))
        {
            return await users.FindByIdAsync(User.GetUserId(), ct) is { } account
                ? AdministratorProblems.Problem(StatusCodes.Status403Forbidden, "step_up_required",
                    $"Confirm it's you before issuing '{role.Name}'.", account.TotpEnabled ? "code" : "sign_in")
                : UiApiProblem.StaleSession();
        }

        if (!role.IsEnabled)
        {
            return AdministratorProblems.Problem(StatusCodes.Status409Conflict, "role_disabled",
                $"The role '{role.Name}' is disabled and cannot issue credentials.");
        }

        try
        {
            var issued = await dynamicSecrets.IssueAsync(role.Id, request.TtlMinutes, ct);
            return Ok(new IssuedDynamicCredentialResponse(issued.LeaseId, issued.RoleName, issued.Identity, issued.Secret, issued.ExpiresAtUtc));
        }
        catch (PremiumFeatureNotLicensedException)
        {
            return AdministratorProblems.Problem(StatusCodes.Status402PaymentRequired, "premium_not_licensed", NotLicensedMessage);
        }
        catch (MintRefusedException refused)
        {
            // The backend logged its own error by type and number; its message could quote the password.
            logger.LogWarning("Minting a dynamic credential for role {RoleName} failed; nothing was issued", role.Name);
            return AdministratorProblems.Problem(StatusCodes.Status502BadGateway, "mint_refused", refused.Message);
        }
        catch (MintIncompleteException incomplete)
        {
            LogLoginMayRemain(incomplete.RoleName, incomplete.Login);
            return AdministratorProblems.Problem(StatusCodes.Status502BadGateway, "mint_incomplete", MintIncompleteException.FailureMessage);
        }
        catch (MintUnrecordedException unrecorded)
        {
            // Still an audit failure: the global handler answers 503 audit_unavailable.
            LogLoginMayRemain(unrecorded.RoleName, unrecorded.Login);
            throw;
        }
    }

    [HttpPost("leases/{leaseId:guid}/revoke")]
    [ProducesResponseType<RevokedLeaseResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Revoke(Guid leaseId, CancellationToken ct)
    {
        LeaseStatus? closed;
        try
        {
            closed = await dynamicSecrets.RevokeAsync(leaseId, User.GetUserId(), User.IsAdmin(), ct);
        }
        catch (RevokedUnrecordedException)
        {
            return AdministratorProblems.Problem(StatusCodes.Status503ServiceUnavailable, "revoked_unrecorded", RevokedUnrecordedException.FailureMessage);
        }

        if (closed is not { } status)
        {
            // One answer for unknown, closed and someone else's, so revoke cannot probe for leases.
            return AdministratorProblems.Problem(StatusCodes.Status404NotFound, "lease_not_found",
                "That lease is not active, or is not yours to revoke.");
        }

        var note = status == LeaseStatus.RevocationFailed
            ? FixedNote((await dynamicSecrets.FindLeaseAsync(leaseId, ct))?.RevocationError) ?? DynamicSecretService.BackendRefusedNote
            : null;
        return Ok(new RevokedLeaseResponse(status.ToString(), note));
    }

    /// <summary>The licence note's facts. The evaluation read fails open: a failure leaves the note out.</summary>
    private async Task<DynamicSecretsLicenceResponse?> LicenceAsync(CancellationToken ct)
    {
        if (license.Allows(LicenseFeatures.DynamicSecrets))
        {
            return new DynamicSecretsLicenceResponse(true, null);
        }

        try
        {
            return new DynamicSecretsLicenceResponse(false, await evaluations.DaysRemainingAsync(LicenseFeatures.DynamicSecrets, clock, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reading the dynamic secrets evaluation window failed; the licence note is left out");
            return null;
        }
    }

    /// <summary>
    /// A lease closed before notes were fixed sentences may hold the backend's own error text, which can name
    /// the target and belongs only in the log.
    /// </summary>
    private static string? FixedNote(string? stored)
        => stored is null or DynamicSecretService.RoleMissingNote ? stored : DynamicSecretService.BackendRefusedNote;

    private void LogLoginMayRemain(string roleName, string login)
        => logger.LogCritical(
            "A login minted for dynamic role {RoleName} may remain on the target: {Login}. Check the target and drop it (docs/INCIDENT_RESPONSE.md)",
            roleName, login);
}
