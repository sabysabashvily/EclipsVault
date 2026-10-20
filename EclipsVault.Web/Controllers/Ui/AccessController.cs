using EclipsVault.Core.Application.Abac;
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
/// Self-service "My access": explains, in plain language, what secrets the signed-in user can open
/// right now and — where they can't — exactly why. It answers the most common question in an
/// attribute-based system ("why can't I see this?") without anyone having to read the policy code.
///
/// It is computed from the <b>same</b> pure rule engine (<see cref="SecretAccessPolicy"/>) and the
/// <b>same</b> environmental snapshot (<see cref="IAccessContextProvider"/>) that actually guard
/// secrets, so the explanation can never drift from what is enforced. Strictly self-scoped: it only
/// ever evaluates the caller's own clearance and project, and discloses no secrets, no other users,
/// and not even the trusted-network ranges (only whether the current network is trusted).
/// </summary>
[ApiController]
[Route("api/ui/access")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
public sealed class AccessController : ControllerBase
{
    // Rows run from most to least sensitive so the clearance cut-off reads top-to-bottom; columns
    // are the deployment environments.
    private static readonly SensitivityLevel[] Sensitivities =
        [SensitivityLevel.TopSecret, SensitivityLevel.Secret, SensitivityLevel.Confidential, SensitivityLevel.Internal];

    private static readonly SecretEnvironment[] Environments =
        [SecretEnvironment.Development, SecretEnvironment.Staging, SecretEnvironment.Production];

    private readonly IAccessContextProvider _accessContext;
    private readonly IAntiforgery _antiforgery;

    public AccessController(IAccessContextProvider accessContext, IAntiforgery antiforgery)
    {
        _accessContext = accessContext;
        _antiforgery = antiforgery;
    }

    [HttpGet]
    public async Task<ActionResult<AccessResponse>> Get(CancellationToken ct)
    {
        var projectClaim = User.GetProject();
        if (User.GetClearanceOrNull() is not { } clearance || projectClaim.Length == 0)
        {
            // Missing vault attribute claims — a stale session; send it to sign out.
            UiAntiforgery.IssueToken(HttpContext, _antiforgery);
            return UiApiProblem.StaleSession();
        }

        var context = await _accessContext.CurrentAsync(ct);

        // The context that ABAC would see for this request. Project is set to the user's own so the
        // grid isolates the clearance/environment/network rules; cross-project access is explained
        // separately (it depends on the individual secret's project or an explicit grant).
        var requestContext = new RequestContext(
            DateTimeOffset.UtcNow,
            context.SourceIp?.ToString(),
            context.IsWithinProductionWindow,
            context.IsTrustedNetwork);

        var rows = Sensitivities.Select(sensitivity =>
        {
            var cells = Environments.Select(environment =>
            {
                var resource = new ResourceAttributes(environment, sensitivity, projectClaim);
                var decision = SecretAccessPolicy.Evaluate(
                    new SubjectAttributes(clearance, projectClaim), resource, requestContext);
                return new AccessCellResponse(environment.ToString(), decision.IsAllowed, ShortReason(decision));
            }).ToList();
            return new AccessRowResponse(sensitivity.ToDisplayName(), (int)sensitivity, cells);
        }).ToList();

        return new AccessResponse(
            clearance.ToDisplayName(),
            (int)clearance,
            projectClaim,
            clearance == ClearanceLevel.TopSecret,
            context.SourceIp?.ToString() ?? "unknown",
            context.IsTrustedNetwork,
            context.IsWithinProductionWindow,
            context.WindowStartHour,
            context.WindowEndHour,
            context.WindowZoneLabel,
            Environments.Select(environment => environment.ToString()).ToArray(),
            rows);
    }

    /// <summary>
    /// A short, human tag for a denial, derived from the policy's own reason so the decision stays
    /// single-sourced. Null when allowed.
    /// </summary>
    private static string? ShortReason(AccessDecision decision)
    {
        if (decision.IsAllowed)
        {
            return null;
        }

        var reason = decision.DenialReasons[0];
        if (reason.Contains("Clearance", StringComparison.OrdinalIgnoreCase)) return "Clearance too low";
        if (reason.Contains("time window", StringComparison.OrdinalIgnoreCase)) return "Outside access hours";
        if (reason.Contains("trusted network", StringComparison.OrdinalIgnoreCase)) return "Untrusted network";
        if (reason.Contains("project", StringComparison.OrdinalIgnoreCase)) return "Different project";
        return reason;
    }
}
