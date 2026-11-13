using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Creates signed checkpoints over the audit hash chain, verifies the trail against them, and
/// produces a portable, externally verifiable export bundle. Signing turns the trail from
/// "tamper-evident to anyone with the database" into "tamper-evident to anyone with the public key."
/// </summary>
public interface IAuditCheckpointService
{
    /// <summary>Signs the current chain head and persists a checkpoint. Returns null if nothing is chained yet.</summary>
    Task<AuditCheckpointDto?> CreateCheckpointAsync(CancellationToken ct);

    /// <summary>
    /// Verifies the trail against the checkpoints signed by the current key, then records the
    /// verification with its outcome, critical when tampering is found. The outcome is returned only
    /// once it is recorded.
    /// </summary>
    /// <exception cref="AuditBusyException">Another verification is running, on any replica.</exception>
    /// <exception cref="AuditWriteFailedException">The verification could not be recorded.</exception>
    Task<AuditIntegrityReport> VerifyAsync(CancellationToken ct);

    /// <summary>The most recent checkpoint, or null if none has been created.</summary>
    Task<AuditCheckpointDto?> GetLatestAsync(CancellationToken ct);

    /// <summary>Short identifier of the active signing key (for display).</summary>
    string SigningKeyId { get; }

    /// <summary>
    /// Signs the committed head and records the export, then returns a bundle of every chained row up
    /// to that head, with the signed checkpoint and the public key, for an external auditor. Nothing
    /// is returned unless the export is recorded. The export holds its turn until disposed.
    /// </summary>
    /// <exception cref="AuditBusyException">Another export is running, on any replica.</exception>
    /// <exception cref="AuditWriteFailedException">The export could not be recorded.</exception>
    Task<AuditBundleExport> ExportAsync(CancellationToken ct);
}
