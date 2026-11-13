using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Application.Dashboard;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// The signed-in Overview. Reading it writes no audit row. Its secret figures pass through the
/// Secrets list's own filter, so they never describe a secret the caller could not open now.
/// </summary>
[ApiController]
[Route("api/ui/overview")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OverviewController(
    IDashboardService overview, ISecretService secrets, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<OverviewResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<OverviewResponse>> Get(CancellationToken ct)
    {
        var isAdministrator = User.IsAdmin();
        // Listing through the service also leaves out decoys.
        var visible = await authorization.VisibleToAsync(User, await secrets.ListAsync(ct));
        var snapshot = await overview.GetAsync(visible, User.GetUserId(), isAdministrator, ct);

        return new OverviewResponse(
            User.GetDisplayName(),
            snapshot.TotalActiveSecrets,
            snapshot.DevelopmentCount,
            snapshot.StagingCount,
            snapshot.ProductionCount,
            snapshot.ExpiringWithin7Days,
            snapshot.CriticalEventsLast24h,
            snapshot.StaffAccounts,
            isAdministrator ? "vault" : "own",
            snapshot.RecentEvents.Select(EventOf).ToList(),
            snapshot.ExpiringSoon.Select(secret => new ExpiringSecretResponse(
                secret.Id, secret.Name, secret.Environment.ToString(), secret.ExpiresAtUtc.UtcDateTime)).ToList());
    }

    private static OverviewEventResponse EventOf(AuditEntryDto entry)
    {
        var (label, tone) = AuditDisplay.For(entry.Action);
        return new OverviewEventResponse(entry.TimestampUtc.UtcDateTime, entry.Action.ToString(), label, tone,
            entry.ResourceName ?? entry.ResourceType, entry.Username, entry.IsCritical);
    }
}
