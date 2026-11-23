using System.ComponentModel.DataAnnotations;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// The Overview. Secret figures cover only the secrets the caller could open now.
/// <paramref name="StaffAccounts"/> is null for anyone but an administrator, and
/// <paramref name="ActivityScope"/> says whether <paramref name="RecentEvents"/> is the vault's or the caller's own.
/// </summary>
public sealed record OverviewResponse(
    string DisplayName,
    int TotalActiveSecrets,
    int DevelopmentCount,
    int StagingCount,
    int ProductionCount,
    int ExpiringWithin7Days,
    int CriticalEventsLast24h,
    int? StaffAccounts,
    [property: AllowedValues("vault", "own")] string ActivityScope,
    IReadOnlyList<OverviewEventResponse> RecentEvents,
    IReadOnlyList<ExpiringSecretResponse> ExpiringSoon);

/// <summary>One recent audit entry. <paramref name="Resource"/> is the resource's name, or its type when it has none.</summary>
public sealed record OverviewEventResponse(
    DateTime TimestampUtc,
    string Action,
    string ActionLabel,
    [property: AllowedValues("ok", "muted", "warn", "danger", "critical")] string Tone,
    string Resource,
    string Actor,
    bool IsCritical);

/// <summary>A secret the caller could open that expires within 7 days, soonest first.</summary>
public sealed record ExpiringSecretResponse(
    Guid SecretId,
    string Name,
    [property: UiEnum(typeof(SecretEnvironment))] string Environment,
    DateTime ExpiresAtUtc);
