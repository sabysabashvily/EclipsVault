using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Dashboard;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.Web;

/// <summary>
/// Overview figures must describe only the caller's visible secrets: even a name or expiry can
/// disclose a secret in another project. The service receives the filtered list rather than a
/// repository so it cannot enumerate secrets the caller cannot open. Accounts and the vault's
/// activity are an administrator's alone.
/// </summary>
public class DashboardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Administrator = Guid.NewGuid();

    private static SecretSummaryDto Secret(string name, string project, DateTimeOffset? expires) =>
        new(Guid.NewGuid(), name, project, SecretEnvironment.Production, SensitivityLevel.Confidential,
            Now.AddDays(-10), expires);

    private static DashboardService Build() =>
        new(new FakeUsers(), new FakeAudit(Alice, own: [], vault: []), new FixedClock(Now));

    private static AuditEntryDto Reveal(string username, string secret) =>
        new(Guid.NewGuid(), Now.AddMinutes(-5), username, "10.0.0.5", AuditAction.SecretRevealed, "Secret",
            secret, Details: null, IsCritical: false);

    private static AuditEntryDto Denial(string username) =>
        new(Guid.NewGuid(), Now.AddMinutes(-5), username, "10.0.0.5", AuditAction.SecretAccessDenied, "Secret",
            "PAYMENTS_DB_PASSWORD", "Clearance 'Standard' is below required sensitivity 'Secret'.", IsCritical: false);

    [Fact]
    public async Task A_non_administrators_overview_loads_no_accounts()
    {
        var users = new FakeUsers();

        var dto = await new DashboardService(users, new FakeAudit(Alice, own: [], vault: []), new FixedClock(Now))
            .GetAsync([], Alice, isAdministrator: false, CancellationToken.None);

        Assert.Equal(0, users.Listed);
        Assert.Null(dto.StaffAccounts);
    }

    [Fact]
    public async Task An_administrators_overview_counts_the_staff_accounts()
    {
        var users = new FakeUsers(new User(), new User(), new User());

        var dto = await new DashboardService(users, new FakeAudit(Alice, own: [], vault: []), new FixedClock(Now))
            .GetAsync([], Administrator, isAdministrator: true, CancellationToken.None);

        Assert.Equal(3, dto.StaffAccounts);
    }

    [Fact]
    public async Task A_non_administrator_gets_the_activity_of_their_own_account()
    {
        var audit = new FakeAudit(Alice, own: [Reveal("alice", "ALICE_API_KEY")], vault: [Reveal("bob", "BOB_DB_PASSWORD")]);

        var dto = await new DashboardService(new FakeUsers(), audit, new FixedClock(Now))
            .GetAsync([], Alice, isAdministrator: false, CancellationToken.None);

        Assert.Equal(["ALICE_API_KEY"], dto.RecentEvents.Select(entry => entry.ResourceName));
    }

    [Fact]
    public async Task An_administrator_gets_the_vaults_activity()
    {
        var audit = new FakeAudit(Alice, own: [Reveal("alice", "ALICE_API_KEY")], vault: [Reveal("bob", "BOB_DB_PASSWORD")]);

        var dto = await new DashboardService(new FakeUsers(), audit, new FixedClock(Now))
            .GetAsync([], Administrator, isAdministrator: true, CancellationToken.None);

        Assert.Equal(["BOB_DB_PASSWORD"], dto.RecentEvents.Select(entry => entry.ResourceName));
    }

    [Fact]
    public async Task A_denial_in_a_non_administrators_own_activity_does_not_name_the_secret()
    {
        var audit = new FakeAudit(Alice, own: [Denial("alice")], vault: []);

        var dto = await new DashboardService(new FakeUsers(), audit, new FixedClock(Now))
            .GetAsync([], Alice, isAdministrator: false, CancellationToken.None);

        var denial = Assert.Single(dto.RecentEvents);
        Assert.Null(denial.ResourceName);
        Assert.Equal("Secret", denial.ResourceType);
    }

    [Fact]
    public async Task A_denial_in_the_vaults_activity_names_the_secret_for_an_administrator()
    {
        var audit = new FakeAudit(Alice, own: [], vault: [Denial("alice")]);

        var dto = await new DashboardService(new FakeUsers(), audit, new FixedClock(Now))
            .GetAsync([], Administrator, isAdministrator: true, CancellationToken.None);

        Assert.Equal("PAYMENTS_DB_PASSWORD", Assert.Single(dto.RecentEvents).ResourceName);
    }

    [Fact]
    public async Task The_expiring_count_covers_every_expiring_secret_while_the_list_stops_at_eight()
    {
        SecretSummaryDto[] visible =
        [
            Secret("EXPIRING_1", "WEB", Now.AddHours(1)),
            Secret("EXPIRING_2", "WEB", Now.AddHours(2)),
            Secret("EXPIRING_3", "WEB", Now.AddHours(3)),
            Secret("EXPIRING_4", "WEB", Now.AddHours(4)),
            Secret("EXPIRING_5", "WEB", Now.AddHours(5)),
            Secret("EXPIRING_6", "WEB", Now.AddHours(6)),
            Secret("EXPIRING_7", "WEB", Now.AddHours(7)),
            Secret("EXPIRING_8", "WEB", Now.AddHours(8)),
            Secret("EXPIRING_9", "WEB", Now.AddHours(9)),
        ];

        var dto = await Build().GetAsync(visible, Alice, isAdministrator: false, CancellationToken.None);

        Assert.Equal(9, dto.ExpiringWithin7Days);
        Assert.Equal(8, dto.ExpiringSoon.Count);
    }

    [Fact]
    public async Task Expiring_soon_lists_only_the_secrets_the_caller_can_see()
    {
        var visible = new[] { Secret("WEB_TLS_CERT", "WEB", Now.AddDays(2)) };

        var dto = await Build().GetAsync(visible, Alice, isAdministrator: false, CancellationToken.None);

        Assert.Single(dto.ExpiringSoon);
        Assert.Equal("WEB_TLS_CERT", dto.ExpiringSoon[0].Name);
    }

    [Fact]
    public async Task The_active_secret_count_is_the_callers_own_view_not_the_whole_vault()
    {
        var visible = new[]
        {
            Secret("WEB_TLS_CERT", "WEB", Now.AddDays(2)),
            Secret("WEB_SMTP_PASSWORD", "WEB", null)
        };

        var dto = await Build().GetAsync(visible, Alice, isAdministrator: false, CancellationToken.None);

        Assert.Equal(2, dto.TotalActiveSecrets);
        Assert.Equal(2, dto.ProductionCount);
        Assert.Equal(1, dto.ExpiringWithin7Days);
    }

    [Fact]
    public async Task A_caller_who_can_see_nothing_gets_an_empty_dashboard_not_the_vault_total()
    {
        var dto = await Build().GetAsync([], Alice, isAdministrator: false, CancellationToken.None);

        Assert.Equal(0, dto.TotalActiveSecrets);
        Assert.Empty(dto.ExpiringSoon);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeUsers(params User[] accounts) : IUserRepository
    {
        public int Listed { get; private set; }

        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct)
        {
            Listed++;
            return Task.FromResult<IReadOnlyList<User>>(accounts);
        }

        public Task<bool> AnyOtherEnabledAdministratorAsync(Guid accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ReloadAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public void StageAddition(User user) => throw new NotSupportedException();
        public void StageRemoval(User user) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarAsync(User user, byte[] png, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarRemovalAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryAcceptTotpStepAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryCompleteTotpEnrollmentAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RecordFailedAttemptAsync(User user, LockoutPolicy lockout, DateTimeOffset nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearFailedAttemptsAsync(User user, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>The vault's newest rows, and the rows of one account, which only its id finds.</summary>
    private sealed class FakeAudit(Guid account, AuditEntryDto[] own, AuditEntryDto[] vault) : IAuditLogReader
    {
        public Task<IReadOnlyList<AuditEntryDto>> ListRecentAsync(int count, string? username, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AuditEntryDto>>(username is null ? vault : []);

        public Task<IReadOnlyList<AuditEntryDto>> ListForActorAsync(Guid actorUserId, int skip, int take, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AuditEntryDto>>(actorUserId == account ? own : []);

        public Task<int> CountCriticalSinceAsync(DateTimeOffset sinceUtc, CancellationToken ct)
            => Task.FromResult(0);

        public Task<IReadOnlyList<AuditEntryDto>> ListForActorByActionsAsync(Guid actorUserId, IReadOnlyCollection<AuditAction> actions, int take, CancellationToken ct) => throw new NotSupportedException();
    }
}
