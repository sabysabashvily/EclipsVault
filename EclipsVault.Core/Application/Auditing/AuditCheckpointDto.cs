namespace EclipsVault.Core.Application.Auditing;

/// <summary>A persisted checkpoint, for display.</summary>
public sealed record AuditCheckpointDto(long Sequence, string ChainHeadHash, DateTimeOffset CreatedAtUtc, string SigningKeyId);
