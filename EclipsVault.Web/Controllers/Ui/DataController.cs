using System.Text;
using System.Text.Json;
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
/// Your data: what the vault holds about the signed-in vault account, and its portable copy. The
/// account comes from the session alone, and the request chooses nothing. The export is recorded
/// before the file is handed over, so an export the audit trail cannot hold never leaves the vault.
/// </summary>
[ApiController]
[Route("api/ui/data")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DataController(IPersonalDataExportService export, IAuditSink audit, IAntiforgery antiforgery) : ControllerBase
{
    // Indented for humans; default (HTML-safe) escaping since the payload carries user-controlled
    // strings and is served for download rather than embedded anywhere.
    private static readonly JsonSerializerOptions ExportJson = new() { WriteIndented = true };

    [HttpGet]
    [ProducesResponseType<DataResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<DataResponse>> Get(CancellationToken ct)
    {
        if (await ExportForSessionAsync(ct) is not { } data)
        {
            return StaleSession();
        }

        return new DataResponse(
            new DataAccountResponse(
                data.Account.Username,
                data.Account.DisplayName,
                data.Account.Email,
                data.Account.Clearance,
                data.Account.ProjectKey,
                data.Account.HasCustomAvatar),
            data.Security.TwoStepEnabled,
            data.Security.Passkeys.Count,
            data.Security.BackupCodesRemaining,
            data.SignedInDevices.Count,
            data.AccessRequests.Count,
            data.RecentActivity.Count,
            data.Notice);
    }

    /// <summary>The portable copy: export schema 1.0 as indented JSON, under a dated attachment name, never cached.</summary>
    [HttpPost("export")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        if (await ExportForSessionAsync(ct) is not { } data)
        {
            return StaleSession();
        }

        // A failed write throws before any header of the file is set: the caller gets 503 audit_unavailable.
        await audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.PersonalDataExported,
            ResourceType = "Account",
            ResourceId = User.GetUserId(),
            Details = $"Exported personal data ({data.RecentActivity.Count} activity entries)"
        }, ct);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, ExportJson);

        // The bundle mirrors sensitive account data; keep it out of any shared/browser cache.
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";

        var stamp = data.GeneratedAtUtc.UtcDateTime.ToString("yyyyMMdd");
        var fileName = $"eclipsvault-data-{SafeSlug(data.Account.Username)}-{stamp}.json";

        // File(...) sets Content-Disposition: attachment; combined with the global nosniff header the
        // browser downloads it rather than rendering it.
        return File(bytes, "application/json", fileName);
    }

    /// <summary>Null when the session carries no account id, or its account is gone.</summary>
    private async Task<PersonalDataExport?> ExportForSessionAsync(CancellationToken ct)
    {
        var userId = User.GetUserId();
        return userId == Guid.Empty ? null : await export.BuildForUserAsync(userId, ct);
    }

    private ActionResult StaleSession()
    {
        UiAntiforgery.IssueToken(HttpContext, antiforgery);
        return UiApiProblem.StaleSession();
    }

    /// <summary>Reduces a username to filename-safe characters (defence against header/path oddities).</summary>
    private static string SafeSlug(string value)
    {
        var safe = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            safe.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-');
        }
        var slug = safe.ToString().Trim('-');
        return slug.Length == 0 ? "account" : slug;
    }
}
