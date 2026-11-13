using EclipsVault.Core.Application.Activity;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Dashboard;

/// <summary>
/// Composes the overview snapshot. Pure aggregation over the secrets it is <em>given</em>, plus the
/// vault-wide critical count, and for an administrator alone the vault-wide activity and the staff
/// account count.
///
/// <para><b>Why it holds no secret repository.</b> A secret's name or expiry discloses that it exists,
/// so the "Expiring soon" table and the counts may describe only secrets the caller could open.
/// Taking the visible set as an argument means the ABAC filter cannot be forgotten: there is no
/// unfiltered source in reach. The caller is responsible for handing in rows that have already
/// been through the same authorization handler that gates opening one.</para>
/// </summary>
public sealed class DashboardService : IDashboardService
{
    private const int RecentEventCount = 10;
    private const int ExpiringListLimit = 8;

    private readonly IUserRepository _users;
    private readonly IAuditLogReader _audit;
    private readonly TimeProvider _clock;

    public DashboardService(IUserRepository users, IAuditLogReader audit, TimeProvider clock)
    {
        _users = users;
        _audit = audit;
        _clock = clock;
    }

    public async Task<DashboardDto> GetAsync(
        IReadOnlyList<SecretSummaryDto> visibleSecrets, Guid userId, bool isAdministrator, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();

        int? staffAccounts = isAdministrator ? (await _users.ListAsync(ct)).Count : null;
        var recent = isAdministrator
            ? await _audit.ListRecentAsync(RecentEventCount, username: null, ct)
            // By account id, as My activity reads it: a username can be reused after its account is deleted.
            : (await _audit.ListForActorAsync(userId, 0, RecentEventCount, ct))
                .Select(entry => entry with { ResourceName = ActivityDescriber.ResourceNameShownToActor(entry.Action, entry.ResourceName) })
                .ToList();
        var criticalLast24h = await _audit.CountCriticalSinceAsync(now.AddHours(-24), ct);

        // Same horizon the lifecycle worker emails on — see SecretExpiry.
        var expiryCutoff = SecretExpiry.SoonCutoff(now);
        var expiringSoon = visibleSecrets
            .Where(s => s.ExpiresAtUtc is { } e && e <= expiryCutoff)
            .OrderBy(s => s.ExpiresAtUtc)
            .Take(ExpiringListLimit)
            .Select(s => new ExpiringSecretDto(s.Id, s.Name, s.Environment, s.ExpiresAtUtc!.Value))
            .ToList();

        return new DashboardDto(
            TotalActiveSecrets: visibleSecrets.Count,
            DevelopmentCount: visibleSecrets.Count(s => s.Environment == SecretEnvironment.Development),
            StagingCount: visibleSecrets.Count(s => s.Environment == SecretEnvironment.Staging),
            ProductionCount: visibleSecrets.Count(s => s.Environment == SecretEnvironment.Production),
            ExpiringWithin7Days: visibleSecrets.Count(s => s.ExpiresAtUtc is { } e && e <= expiryCutoff),
            StaffAccounts: staffAccounts,
            CriticalEventsLast24h: criticalLast24h,
            RecentEvents: recent,
            ExpiringSoon: expiringSoon);
    }
}
