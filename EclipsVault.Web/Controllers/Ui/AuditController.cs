using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Models;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// The audit trail: its newest entries, verification against signed checkpoints, new checkpoints and
/// the signed bundle for an outside auditor. Viewing is recorded before reading the page; Verify
/// records its outcome before answering; Export records before its first byte. A checkpoint commits
/// atomically with its audit row. Recording failure answers 503 <c>audit_unavailable</c>.
/// </summary>
[ApiController]
[Route("api/ui/audit")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.AdminOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuditController(IAuditLogReader reader, IAuditCheckpointService checkpoints, IAuditSink audit, AdministratorStepUp stepUp)
    : ControllerBase
{
    private const int PageSize = 200;

    [HttpGet]
    [ProducesResponseType<AuditResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<AuditResponse>> Get(CancellationToken ct)
    {
        await audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.AuditLogViewed,
            ResourceType = "AuditTrail",
            Details = $"Opened the newest {PageSize} entries"
        }, ct);

        var entries = await reader.ListRecentAsync(PageSize, username: null, ct);
        return new AuditResponse(entries.Select(EntryOf).ToList(), checkpoints.SigningKeyId,
            await checkpoints.GetLatestAsync(ct) is { } latest ? CheckpointOf(latest) : null);
    }

    /// <summary>Verifies the trail against its signed checkpoints; the outcome is answered only once it is recorded.</summary>
    [HttpPost("verify")]
    [ProducesResponseType<AuditVerificationResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Verify(CancellationToken ct)
    {
        AuditIntegrityReport report;
        try
        {
            report = await checkpoints.VerifyAsync(ct);
        }
        catch (AuditBusyException)
        {
            return Busy();
        }

        return Ok(new AuditVerificationResponse(report.Outcome.ToString().ToLowerInvariant(), report.EntriesVerified,
            report.AnchoredAtSequence, report.FirstBrokenSequence, report.CheckpointsChecked, report.CheckpointsNotChecked, report.Message));
    }

    [HttpPost("checkpoints")]
    [ProducesResponseType<AuditCheckpointResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> CreateCheckpoint(CancellationToken ct)
        => await checkpoints.CreateCheckpointAsync(ct) is { } checkpoint
            ? StatusCode(StatusCodes.Status201Created, CheckpointOf(checkpoint))
            : AdministratorProblems.Problem(StatusCodes.Status409Conflict, "nothing_to_checkpoint",
                "There are no audit entries to checkpoint yet.");

    /// <summary>
    /// Streams the signed bundle of the whole trail once the export is recorded. It verifies offline
    /// with the <c>EclipsVault.AuditVerifier</c> tool, against the vault's public key alone.
    /// </summary>
    [HttpPost("export")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        if (await stepUp.RequireAsync(User, AdministratorAction.ExportAuditTrail, ct) is { } required)
        {
            return required;
        }

        AuditBundleExport export;
        try
        {
            export = await checkpoints.ExportAsync(ct);
        }
        catch (AuditBusyException)
        {
            return Busy();
        }

        // Headers are set only once the export is recorded, so a failure still gets its problem.
        await using (export)
        {
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(export.FileName);
            Response.ContentType = "application/json";
            Response.Headers.ContentDisposition = disposition.ToString();
            await export.WriteToAsync(Response.Body, ct);
        }

        return new EmptyResult();
    }

    private static JsonResult Busy() => AdministratorProblems.Problem(StatusCodes.Status409Conflict, "audit_busy",
        "Another verification or export is running. Try again when it finishes.");

    private static AuditEntryResponse EntryOf(AuditEntryDto entry)
    {
        var (label, tone) = AuditDisplay.For(entry.Action);
        return new AuditEntryResponse(entry.Id, entry.TimestampUtc, entry.Action.ToString(), label, tone,
            entry.ResourceName ?? entry.ResourceType, entry.Username, entry.SourceIp, entry.Details, entry.IsCritical);
    }

    private static AuditCheckpointResponse CheckpointOf(AuditCheckpointDto checkpoint)
        => new(checkpoint.Sequence, checkpoint.CreatedAtUtc, checkpoint.SigningKeyId);
}
