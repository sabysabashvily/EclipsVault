using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Api;

/// <summary>
/// Programmatic secret retrieval for service accounts. Authenticated by API key
/// (Authorization: Bearer evk_… or X-Api-Key). Access is governed by the SAME ABAC
/// policy as the interactive UI — clearance, project, network, and time all apply.
/// </summary>
[ApiController]
[Route("api/v1/secrets")]
[Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
[Produces("application/json")]
public sealed class SecretsApiController : ControllerBase
{
    private readonly ISecretService _secrets;
    private readonly IAuthorizationService _authorization;

    public SecretsApiController(ISecretService secrets, IAuthorizationService authorization)
    {
        _secrets = secrets;
        _authorization = authorization;
    }

    /// <summary>
    /// Lists secret metadata (no values) the calling service account may know exists. Values are
    /// fetched one at a time and gated by the same policy.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        // Every row runs the full rule set — clearance, project, network, time window, and the
        // key's own scope — through the same handler that gates a read. Re-applying any one rule
        // here by hand would be a second copy that could drift, and would tempt skipping the rest.
        var visible = await _authorization.VisibleToAsync(User, await _secrets.ListAsync(ct));

        return Ok(visible.Select(s => new
        {
            id = s.Id,
            name = s.Name,
            project = s.ProjectKey,
            environment = s.Environment.ToString(),
            sensitivity = s.Sensitivity.ToString(),
            expiresAtUtc = s.ExpiresAtUtc
        }));
    }

    /// <summary>Retrieves and decrypts a secret's value if the calling service account is authorized.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        try
        {
            var details = await _secrets.GetDetailsAsync(id, ct);

            var authorized = await _authorization.AuthorizeAsync(User, details, VaultPolicies.SecretAccess);
            if (!authorized.Succeeded)
            {
                var reasons = authorized.Failure?.FailureReasons.Select(r => r.Message).ToList() ?? [];
                await _secrets.RecordAccessDeniedAsync(details, reasons, ct);
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "forbidden", reasons });
            }

            var revealed = await _secrets.RevealAsync(id, ct);
            return Ok(new { id = revealed.Id, name = revealed.Name, value = revealed.Value });
        }
        catch (HoneyTokenTrippedException)
        {
            // The trap already fired (source range blacklisted, critical alert). Give nothing away.
            return NotFound(new { error = "not_found" });
        }
        catch (SecretNotFoundException)
        {
            return NotFound(new { error = "not_found" });
        }
        catch (LegacyBlobRefusedException)
        {
            // The value is sealed in the pre-binding format and the vault refuses to read it until an
            // administrator runs the one-time re-seal. A clean 409 for the API caller — never the
            // interactive path's redirect to an HTML error page.
            return Conflict(new { error = "legacy_blob_refused" });
        }
        catch (AuditWriteFailedException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "audit_unavailable" });
        }
        catch (CryptoConfigurationException)
        {
            // A genuine crypto misconfiguration. Still answer in JSON: an API caller must get a status
            // it can act on, not a 302 into the interactive HTML error page.
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "crypto_unavailable" });
        }
    }
}
