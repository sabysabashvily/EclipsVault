using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Dashboard;

/// <summary>A secret nearing its TTL, surfaced so it can be rotated before it is shredded.</summary>
public sealed record ExpiringSecretDto(Guid Id, string Name, SecretEnvironment Environment, DateTimeOffset ExpiresAtUtc);

public sealed record DashboardDto(
    int TotalActiveSecrets,
    int DevelopmentCount,
    int StagingCount,
    int ProductionCount,
    int ExpiringWithin7Days,
    int? StaffAccounts,
    int CriticalEventsLast24h,
    IReadOnlyList<AuditEntryDto> RecentEvents,
    IReadOnlyList<ExpiringSecretDto> ExpiringSoon);

/// <summary>Aggregates vault state for the overview page.</summary>
public interface IDashboardService
{
    /// <summary>
    /// An administrator gets the vault's activity and the staff account count; anyone else gets only
    /// their own activity, with the names a denial row would disclose withheld, and no account count.
    /// </summary>
    Task<DashboardDto> GetAsync(
        IReadOnlyList<SecretSummaryDto> visibleSecrets, Guid userId, bool isAdministrator, CancellationToken ct);
}
