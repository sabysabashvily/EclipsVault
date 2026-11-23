using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// Which master key wraps each secret's data key, and the rotation that re-wraps them under the
/// current one. Master keys come from the environment or a KMS; only their ids are ever answered.
/// </summary>
[ApiController]
[Route("api/ui/encryption")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.AdminOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EncryptionController(IKekRotationService rotation, ILogger<EncryptionController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<EncryptionResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<EncryptionResponse>> Get(CancellationToken ct)
    {
        var status = await rotation.GetStatusAsync(ct);
        return new EncryptionResponse(status.CurrentKekId, status.KnownKekIds,
            status.Usage.Select(key => new KekResponse(key.KekId, StatusOf(key), key.SecretCount, key.VersionCount)).ToList(),
            status.AwaitingRotation, status.OnMissingKeys);
    }

    [HttpPost("rotate")]
    [ProducesResponseType<RotationResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<RotationStoppedProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Rotate(CancellationToken ct)
    {
        try
        {
            var result = await rotation.RotateAsync(ct);
            return Ok(new RotationResponse(result.CurrentKekId, result.SecretsRewrapped, result.VersionsRewrapped, result.OnMissingKeys));
        }
        catch (KekRotationRunningException)
        {
            return AdministratorProblems.Problem(StatusCodes.Status409Conflict, "rotation_running",
                "Another rotation is already running. Try again when it finishes.");
        }
        catch (KekRotationStoppedException stopped)
        {
            logger.LogCritical(stopped, "Fail-closed: master key rotation stopped because the audit trail is unavailable");
            return RotationStopped(stopped.SecretsRewrapped, stopped.VersionsRewrapped);
        }
    }

    private static JsonResult RotationStopped(int secretsRewrapped, int versionsRewrapped)
        => new(new RotationStoppedProblemResponse
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Code = "audit_unavailable",
            Title = "Rotation stopped because the audit trail could not record it. "
                + $"{secretsRewrapped + versionsRewrapped} item(s) were re-wrapped before it stopped; run it again to continue.",
            SecretsRewrapped = secretsRewrapped,
            VersionsRewrapped = versionsRewrapped,
        })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
            ContentType = "application/problem+json",
        };

    private static string StatusOf(KekUsage key) => key switch
    {
        { IsCurrent: true } => "current",
        { IsKnown: true } => "retired",
        _ => "not_loaded",
    };
}
