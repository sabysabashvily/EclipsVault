using System.ComponentModel.DataAnnotations;

namespace EclipsVault.Web.Models.Ui;

/// <summary>The newest audit entries, newest first, and what the vault signs checkpoints with.</summary>
public sealed record AuditResponse(IReadOnlyList<AuditEntryResponse> Entries, string SigningKeyId, AuditCheckpointResponse? LatestCheckpoint);

/// <summary>One audit entry. <paramref name="Resource"/> is the resource's name, or its type when it has none.</summary>
public sealed record AuditEntryResponse(
    Guid EntryId,
    DateTimeOffset TimestampUtc,
    string Action,
    string ActionLabel,
    [property: AllowedValues("ok", "muted", "warn", "danger", "critical")] string Tone,
    string Resource,
    string Actor,
    string SourceIp,
    string? Details,
    bool IsCritical);

public sealed record AuditCheckpointResponse(long Sequence, DateTimeOffset CreatedAtUtc, string SigningKeyId);

/// <summary>What verifying the trail found. <paramref name="Message"/> is the text that follows the outcome's title.</summary>
public sealed record AuditVerificationResponse(
    [property: AllowedValues("anchored", "unanchored", "tampered")] string Outcome,
    long EntriesVerified,
    long? AnchoredAtSequence,
    long? FirstBrokenSequence,
    int CheckpointsChecked,
    int CheckpointsNotChecked,
    string Message);
