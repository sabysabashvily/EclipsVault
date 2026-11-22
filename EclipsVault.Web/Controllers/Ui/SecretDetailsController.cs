using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EclipsVault.Core.Application.Abac;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Exceptions;
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
/// One secret's page (ADR-0031). Every action looks the secret up (an unknown, expired, shredded or decoy
/// id answers the same 404, and a decoy trips its trap), then runs the access check, and for a change the
/// change right; each refusal writes one <c>SecretAccessDenied</c> row before it answers. Only the page's
/// GET records a view, and each change records only its own event.
/// </summary>
[ApiController]
[Route("api/ui/secrets/{id:guid}")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SecretDetailsController(
    ISecretService secrets,
    ISecretGrantService shares,
    IAuthorizationService authorization,
    IStepUpService stepUp,
    IUserRepository users,
    ILicenseState license,
    IEvaluationWindowStore evaluations,
    TimeProvider clock,
    IAntiforgery antiforgery,
    ILogger<SecretDetailsController> logger) : ControllerBase
{
    /// <summary>The denial row's reason, kept as the change right's Legacy refusal recorded it.</summary>
    private const string NotPermittedReason =
        "Only an administrator or a member of this secret's project can change it. A share grants read access only.";

    [HttpGet]
    [ProducesResponseType<SecretDetailsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var (details, refused) = await OpenAsync(id, change: false, ct);
        if (refused is not null)
        {
            return refused;
        }

        await secrets.RecordViewedAsync(details, ct);

        var mayChange = MayChange(details);
        var versions = await secrets.ListVersionsAsync(id, ct);
        var issued = mayChange ? await shares.ListForSecretAsync(id, ct) : null;
        return Ok(new SecretDetailsResponse(
            details.Id, details.Name, details.ProjectKey, details.Environment.ToString(), (int)details.Sensitivity,
            details.Sensitivity.ToDisplayName(), details.Algorithm, details.CreatedAtUtc, details.UpdatedAtUtc, details.ExpiresAtUtc,
            details.IsManaged ? await ManagedRotationNote.ReadAsync(details.RotationPrincipal!, license, evaluations, clock, logger, ct) : null,
            versions.Select(version => new SecretVersionResponse(
                version.Id, version.VersionNumber, version.ArchivedAtUtc, version.ArchivedBy, version.ChangeNote)).ToList(),
            mayChange, mayChange,
            issued?.Select(share => new SecretShareResponse(
                share.Id, share.GranteeUsername, share.GrantedBy, share.CreatedAtUtc, share.ExpiresAtUtc)).ToList(),
            stepUp.MaxAuthAgeMinutes));
    }

    [HttpPost("reveal")]
    [ProducesResponseType<RevealedValueResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Reveal(Guid id, CancellationToken ct)
    {
        var (details, refused) = await OpenAsync(id, change: false, ct);
        if ((refused ?? await StepUpRequiredAsync(details, "Confirm it's you before revealing this secret.", ct)) is { } answer)
        {
            return answer;
        }

        return Ok(new RevealedValueResponse("current value", (await secrets.RevealAsync(id, ct)).Value));
    }

    [HttpPost("versions/{versionId:guid}/reveal")]
    [ProducesResponseType<RevealedValueResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> RevealVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        var (details, refused) = await OpenAsync(id, change: false, ct);
        if ((refused ?? await StepUpRequiredAsync(details, "Confirm it's you before revealing this secret.", ct)) is { } answer)
        {
            return answer;
        }

        var revealed = await secrets.RevealVersionAsync(id, versionId, ct);
        // A version deleted since its reveal keeps Legacy's fallback label.
        var number = (await secrets.ListVersionsAsync(id, ct)).FirstOrDefault(version => version.Id == versionId)?.VersionNumber;
        return Ok(new RevealedValueResponse(number is { } n ? $"version {n}" : "archived version", revealed.Value));
    }

    [HttpPost("rotate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Rotate(Guid id, [FromBody] RotateSecretUiRequest request, CancellationToken ct)
    {
        var (_, refused) = await OpenAsync(id, change: true, ct);
        if ((refused ?? Invalid(request, "Enter the new value to rotate this secret.")) is { } answer)
        {
            return answer;
        }

        try
        {
            await secrets.RotateAsync(
                id, request.NewValue!, string.IsNullOrWhiteSpace(request.ChangeNote) ? null : request.ChangeNote.Trim(), request.RenewTtlDays, ct);
        }
        catch (DomainException conflict) when (conflict is SecretChangedException or ChangeInProgressException)
        {
            return Conflict(conflict);
        }

        return NoContent();
    }

    [HttpPost("rotate-managed")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status402PaymentRequired, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> RotateManaged(Guid id, [FromBody] RotateManagedUiRequest request, CancellationToken ct)
    {
        var (details, refused) = await OpenAsync(id, change: true, ct);
        if ((refused ?? Invalid(request, "Check the highlighted fields.")) is { } answer)
        {
            return answer;
        }

        try
        {
            await secrets.RotateManagedAsync(id, request.RenewTtlDays, ct);
        }
        catch (PremiumFeatureNotLicensedException)
        {
            return Problem(StatusCodes.Status402PaymentRequired, "premium_not_licensed",
                "The 30-day evaluation period for managed rotation has ended. Rotating the real credential needs a licence — the stored value is unaffected.");
        }
        catch (VaultAdminException unavailable)
        {
            // The secret was read as managed or not just above; a managed one refused here has no backend configured.
            return Problem(StatusCodes.Status409Conflict, details.IsManaged ? "rotation_unavailable" : "not_managed", unavailable.Message);
        }
        catch (SecretChangedException)
        {
            // The upstream password was put back before this answer.
            return Problem(StatusCodes.Status409Conflict, "secret_changed", "This secret changed while you were working on it. Nothing changed.");
        }
        catch (ChangeInProgressException busy)
        {
            return Conflict(busy);
        }
        catch (UpstreamRotationRefusedException refusedUpstream)
        {
            // The backend's own message stays in the log: it can name servers and logins.
            logger.LogError(refusedUpstream.InnerException, "The backend refused the upstream rotation of secret {SecretId}", id);
            return Problem(StatusCodes.Status502BadGateway, "upstream_refused", refusedUpstream.Message);
        }
        catch (UpstreamRotationDriftedException drifted)
        {
            if (!drifted.Recorded)
            {
                logger.LogCritical(drifted, "Secret {SecretId} drifted from its login and the audit trail could not record it", id);
            }

            return Problem(StatusCodes.Status503ServiceUnavailable, "upstream_drifted", drifted.Message);
        }

        return NoContent();
    }

    [HttpPost("versions/{versionId:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Restore(Guid id, Guid versionId, CancellationToken ct)
    {
        var (_, refused) = await OpenAsync(id, change: true, ct);
        if (refused is not null)
        {
            return refused;
        }

        try
        {
            await secrets.RestoreVersionAsync(id, versionId, ct);
        }
        catch (RestoreManagedException managed)
        {
            return Problem(StatusCodes.Status409Conflict, "restore_managed", managed.Message);
        }
        catch (SecretChangedException changed)
        {
            return Conflict(changed);
        }

        return NoContent();
    }

    [HttpPost("shares")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Share(Guid id, [FromBody] ShareSecretUiRequest request, CancellationToken ct)
    {
        var (details, refused) = await OpenAsync(id, change: true, ct);
        if ((refused
             ?? await StepUpRequiredAsync(details, "Confirm it's you before sharing this secret.", ct)
             ?? Invalid(request, "Enter the username or email of the person to share with.")) is { } answer)
        {
            return answer;
        }

        try
        {
            await shares.GrantAsync(id, request.Recipient!, request.TtlDays, ct);
        }
        catch (ShareExistsException exists)
        {
            return Problem(StatusCodes.Status409Conflict, "share_exists", exists.Message);
        }
        catch (SharingException refusedShare)
        {
            return Problem(StatusCodes.Status409Conflict, "share_refused", refusedShare.Message);
        }

        return NoContent();
    }

    [HttpDelete("shares/{shareId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Revoke(Guid id, Guid shareId, CancellationToken ct)
    {
        var (_, refused) = await OpenAsync(id, change: true, ct);
        if (refused is not null)
        {
            return refused;
        }

        return await shares.RevokeAsync(shareId, id, ct)
            ? NoContent()
            : Problem(StatusCodes.Status404NotFound, "share_not_found", "That share no longer exists.");
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (_, refused) = await OpenAsync(id, change: true, ct);
        if (refused is not null)
        {
            return refused;
        }

        await secrets.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// The secret, or the refusal to answer instead: a stale session, the access check's reasons, or for a
    /// change the change right. Both refusals write the denial row first, fail-closed.
    /// </summary>
    private async Task<(SecretDetailsDto Details, IActionResult? Refused)> OpenAsync(Guid id, bool change, CancellationToken ct)
    {
        if (User.GetUserId() == Guid.Empty)
        {
            UiAntiforgery.IssueToken(HttpContext, antiforgery);
            return (null!, UiApiProblem.StaleSession());
        }

        var details = await secrets.GetDetailsAsync(id, ct);
        var access = await authorization.AuthorizeAsync(User, details, VaultPolicies.SecretAccess);
        if (!access.Succeeded)
        {
            var reasons = access.Failure?.FailureReasons.Select(reason => reason.Message).ToList() ?? [];
            await secrets.RecordAccessDeniedAsync(details, reasons, ct);
            return (details, Problem(StatusCodes.Status403Forbidden, "access_denied", "The vault refused access to this secret.", reasons: reasons));
        }

        if (change && !MayChange(details))
        {
            await secrets.RecordAccessDeniedAsync(details, [NotPermittedReason], ct);
            return (details, Problem(StatusCodes.Status403Forbidden, "not_permitted",
                "Only an administrator or a member of this secret's project can change it."));
        }

        return (details, null);
    }

    /// <summary>
    /// The challenge when this secret's sensitivity needs a fresher sign-in than the session's. It writes no
    /// row: the proof that answers it records its own events.
    /// </summary>
    private async Task<IActionResult?> StepUpRequiredAsync(SecretDetailsDto details, string title, CancellationToken ct)
    {
        if (!stepUp.IsRequired(details.Sensitivity, User.GetLastStrongAuthUtc(), clock.GetUtcNow()))
        {
            return null;
        }

        if (await users.FindByIdAsync(User.GetUserId(), ct) is not { } caller)
        {
            UiAntiforgery.IssueToken(HttpContext, antiforgery);
            return UiApiProblem.StaleSession();
        }

        return Problem(StatusCodes.Status403Forbidden, "step_up_required", title, proof: caller.TotpEnabled ? "code" : "sign_in");
    }

    private bool MayChange(SecretDetailsDto details) => SecretChangeRight.Allows(User.IsAdmin(), User.GetProject(), details.ProjectKey);

    /// <summary>The Legacy form's field messages, under the Legacy flash for the whole form; null when the body is valid.</summary>
    private static JsonResult? Invalid(object request, string title)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
        {
            return null;
        }

        var errors = results
            .GroupBy(result => JsonNamingPolicy.CamelCase.ConvertName(result.MemberNames.Single()))
            .ToDictionary(field => field.Key, field => field.Select(result => result.ErrorMessage!).ToArray());
        return Problem(StatusCodes.Status400BadRequest, "validation_failed", title, errors: errors);
    }

    private static JsonResult Conflict(DomainException conflict)
        => Problem(StatusCodes.Status409Conflict, conflict is ChangeInProgressException ? "change_in_progress" : "secret_changed", conflict.Message);

    private static JsonResult Problem(int status, string code, string title, IReadOnlyList<string>? reasons = null,
        IReadOnlyDictionary<string, string[]>? errors = null, string? proof = null)
        => new(new SecretProblemResponse { Status = status, Code = code, Title = title, Reasons = reasons, Errors = errors, Proof = proof })
        {
            StatusCode = status,
            ContentType = "application/problem+json"
        };
}
