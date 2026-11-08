using System.Diagnostics;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

[Authorize(Policy = VaultPolicies.Session)]
public sealed class HomeController : Controller
{
    private readonly IDashboardService _dashboard;
    private readonly ISecretService _secrets;
    private readonly IAuthorizationService _authorization;

    public HomeController(IDashboardService dashboard, ISecretService secrets, IAuthorizationService authorization)
    {
        _dashboard = dashboard;
        _secrets = secrets;
        _authorization = authorization;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return View();
        }

        var isAdmin = User.IsAdmin();

        // Secret names disclose what exists, so dashboard metadata needs the same access filter
        // as the Secrets list. Listing through the service also excludes honey tokens.
        var visible = await _authorization.VisibleToAsync(User, await _secrets.ListAsync(ct));

        var dto = await _dashboard.GetAsync(visible, isAdmin ? null : User.Identity.Name, ct);
        var displayName = User.GetDisplayName();

        return View("Dashboard", new DashboardViewModel
        {
            Username = displayName,
            IsAdmin = isAdmin,
            TotalActiveSecrets = dto.TotalActiveSecrets,
            DevelopmentCount = dto.DevelopmentCount,
            StagingCount = dto.StagingCount,
            ProductionCount = dto.ProductionCount,
            ExpiringWithin7Days = dto.ExpiringWithin7Days,
            UserCount = dto.UserCount,
            CriticalEventsLast24h = dto.CriticalEventsLast24h,
            RecentEvents = dto.RecentEvents,
            ExpiringSoon = dto.ExpiringSoon
        });
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Error(int? code)
    {
        var (title, message) = code switch
        {
            404 => ("Not found", "The requested resource does not exist, has expired, or has been shredded."),
            403 => ("Access denied", "The attribute-based access policy denied this request."),
            409 => ("Secret needs a one-time upgrade", "This value was sealed before the vault bound each secret to its row, so it cannot be safely read until an administrator completes the re-seal migration. Nothing was decrypted or exposed."),
            503 => ("Vault unavailable (fail-closed)", "The audit trail could not be written, so the operation was refused. No data was released."),
            402 => ("Licence required", "This feature's 30-day evaluation period has ended. Install a licence, then try again. Nothing was changed, and nothing already running was stopped."),
            _ => ("Something went wrong", "An unexpected error occurred. The incident has been logged.")
        };

        Response.StatusCode = code ?? 500;
        return View(new ErrorViewModel(code ?? 500, title, message, Activity.Current?.Id ?? HttpContext.TraceIdentifier));
    }
}
