using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

/// <summary>Viewer over the immutable audit trail, plus signed-checkpoint attestation and export.</summary>
[Authorize(Policy = VaultPolicies.AdminOnly)]
public sealed class AuditController : Controller
{
    private const int PageSize = 200;

    private const string AuditTrail = "AuditTrail";

    private static readonly JsonSerializerOptions BundleJson = new() { WriteIndented = true };

    private readonly IAuditLogReader _reader;
    private readonly IAuditCheckpointService _checkpoints;
    private readonly IAuditSink _audit;

    public AuditController(IAuditLogReader reader, IAuditCheckpointService checkpoints, IAuditSink audit)
    {
        _reader = reader;
        _checkpoints = checkpoints;
        _audit = audit;
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

    /// <summary>Re-walks the hash chain and shows whether the trail has been tampered with.</summary>
    [HttpPost]
    public async Task<IActionResult> Verify(CancellationToken ct)
    {
        var report = await _reader.VerifyIntegrityAsync(ct);
        await _audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.AuditChainVerified,
            ResourceType = AuditTrail,
            Details = report.Intact
                ? report.Message
                : $"Tampering detected. {report.Message} (First broken entry: #{report.FirstBrokenSequence}.)",
            IsCritical = !report.Intact
        }, ct);

        return View(nameof(Index), await BuildAsync(report, ct));
    }

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
    /// Downloads a self-contained, signed bundle of the whole audit trail. It can be verified
    /// offline with the <c>EclipsVault.AuditVerifier</c> tool — no access to this app or its
    /// database required.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var bundle = await _checkpoints.ExportAsync(ct);
        var json = JsonSerializer.SerializeToUtf8Bytes(bundle, BundleJson);
        var fileName = $"eclipsvault-audit-bundle-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json";
        return File(json, "application/json", fileName);
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
