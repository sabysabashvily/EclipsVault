using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Auditing;

public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string Username,
    string SourceIp,
    AuditAction Action,
    string ResourceType,
    string? ResourceName,
    string? Details,
    bool IsCritical);
