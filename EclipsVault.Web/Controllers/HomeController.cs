using System.Diagnostics;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Hosting;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

[Authorize(Policy = VaultPolicies.Session)]
public sealed class HomeController(IWebHostEnvironment environment) : Controller
{
    /// <summary>The Overview, served by the Angular UI, to a signed-in session; the landing page to anyone else.</summary>
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return View();
        }

        return PhysicalFile(AngularUiRoutes.PrepareIndex(HttpContext, environment), "text/html; charset=utf-8");
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
