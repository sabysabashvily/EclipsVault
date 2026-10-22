using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Application.Dashboard;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.Web;

/// <summary>
/// Dashboard figures must describe only the caller's visible secrets: even a name or expiry can
/// disclose a secret in another project. The service receives the filtered list rather than a
/// repository so it cannot enumerate secrets the caller cannot open.
/// </summary>
public class DashboardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private static SecretSummaryDto Secret(string name, string project, DateTimeOffset? expires) =>
        new(Guid.NewGuid(), name, project, SecretEnvironment.Production, SensitivityLevel.Confidential,
            Now.AddDays(-10), expires);

    private static DashboardService Build() =>
        new(new FakeUsers(), new FakeAudit(), new FixedClock(Now));


    [Fact]
    public async Task Expiring_soon_lists_only_the_secrets_the_caller_can_see()
    {
        var visible = new[] { Secret("WEB_TLS_CERT", "WEB", Now.AddDays(2)) };

        var dto = await Build().GetAsync(visible, restrictActivityToUsername: "alice", CancellationToken.None);

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

        var dto = await Build().GetAsync(visible, restrictActivityToUsername: "alice", CancellationToken.None);

        Assert.Equal(2, dto.TotalActiveSecrets);
        Assert.Equal(2, dto.ProductionCount);
        Assert.Equal(1, dto.ExpiringWithin7Days);
    }

    [Fact]
    public async Task A_caller_who_can_see_nothing_gets_an_empty_dashboard_not_the_vault_total()
    {
        var dto = await Build().GetAsync([], restrictActivityToUsername: "alice", CancellationToken.None);

        Assert.Equal(0, dto.TotalActiveSecrets);
        Assert.Empty(dto.ExpiringSoon);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeUsers : IUserRepository
    {
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<User>>([]);

        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task SetAvatarAsync(User user, byte[] png, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveAvatarAsync(User user, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeAudit : IAuditLogReader
    {
        public Task<IReadOnlyList<AuditEntryDto>> ListRecentAsync(int count, string? username, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AuditEntryDto>>([]);

        public Task<int> CountCriticalSinceAsync(DateTimeOffset sinceUtc, CancellationToken ct)
            => Task.FromResult(0);

        public Task<IReadOnlyList<AuditEntryDto>> ListForActorAsync(Guid actorUserId, int skip, int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditEntryDto>> ListForActorByActionsAsync(Guid actorUserId, IReadOnlyCollection<AuditAction> actions, int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<AuditIntegrityReport> VerifyIntegrityAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
