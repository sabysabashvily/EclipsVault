using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace EclipsVault.Web.Controllers;

/// <summary>Viewer over the immutable audit trail, plus signed-checkpoint attestation and export.</summary>
[Authorize(Policy = VaultPolicies.AdminOnly)]
public sealed class AuditController : Controller
{
    private const int PageSize = 200;

    private const string AuditTrail = "AuditTrail";

    private readonly IAuditLogReader _reader;
    private readonly IAuditCheckpointService _checkpoints;
    private readonly IAuditSink _audit;
    private readonly AdministratorStepUp _stepUp;

    public AuditController(IAuditLogReader reader, IAuditCheckpointService checkpoints, IAuditSink audit, AdministratorStepUp stepUp)
    {
        _reader = reader;
        _checkpoints = checkpoints;
        _audit = audit;
        _stepUp = stepUp;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Recorded before any entry is read: if the row cannot be written, the page is not shown.
        await _audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.AuditLogViewed,
            ResourceType = AuditTrail,
            Details = $"Opened the newest {PageSize} entries"
        }, ct);

        return View(await BuildAsync(integrity: null, ct));
    }

    /// <summary>Verifies the trail against its signed checkpoints and shows what was found, once it is recorded.</summary>
    [HttpPost]
    public async Task<IActionResult> Verify(CancellationToken ct)
        => View(nameof(Index), await BuildAsync(await _checkpoints.VerifyAsync(ct), ct));

    /// <summary>Signs the current chain head so its integrity can later be proven to an outside party.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateCheckpoint(CancellationToken ct)
    {
        var checkpoint = await _checkpoints.CreateCheckpointAsync(ct);
        if (checkpoint is null)
        {
            this.FlashInfo("There are no audit entries to checkpoint yet.");
        }
        else
        {
            this.FlashSuccess($"Signed checkpoint created at sequence {checkpoint.Sequence} with key {checkpoint.SigningKeyId}.");
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Downloads a self-contained, signed bundle of the whole audit trail, streamed once the export is
    /// recorded. It can be verified offline with the <c>EclipsVault.AuditVerifier</c> tool — no access
    /// to this app or its database required.
    /// </summary>
    [HttpPost]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        if (await _stepUp.RequireAsync(User, AdministratorAction.ExportAuditTrail, ct) is { } required)
        {
            return required;
        }

        // Headers are set only once the export is recorded, so a failure still gets the error page.
        await using var export = await _checkpoints.ExportAsync(ct);
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(export.FileName);
        Response.ContentType = "application/json";
        Response.Headers.ContentDisposition = disposition.ToString();
        await export.WriteToAsync(Response.Body, ct);
        return new EmptyResult();
    }

    private async Task<AuditIndexViewModel> BuildAsync(AuditIntegrityReport? integrity, CancellationToken ct)
        => new()
        {
            Entries = await _reader.ListRecentAsync(PageSize, username: null, ct),
            Integrity = integrity,
            LatestCheckpoint = await _checkpoints.GetLatestAsync(ct),
            SigningKeyId = _checkpoints.SigningKeyId
        };
}
