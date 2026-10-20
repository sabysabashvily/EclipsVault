using EclipsVault.Core.Application.Sessions;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.Sessions;

/// <summary>
/// The session-registry contract that both the in-process and Redis stores must honour: sessions
/// are per-account, an individual revoke removes one and cannot be undone by the cookie coming back,
/// expired sessions drop off, and the last-seen touch is throttled without losing created-at.
/// </summary>
public class InMemorySessionRegistryTests
{
    private static InMemorySessionRegistry CreateRegistry()
        => new(NullLogger<InMemorySessionRegistry>.Instance);

    private static SessionObservation Observation(
        Guid userId, Guid sessionId, string device = "Chrome on macOS", string ip = "203.0.113.5",
        DateTimeOffset? seen = null, DateTimeOffset? expires = null)
        => new(userId, sessionId, device, ip, seen ?? DateTimeOffset.UtcNow, expires ?? DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public async Task A_recorded_session_is_listed_for_its_owner()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await registry.RecordSeenAsync(Observation(userId, sessionId, device: "Firefox on Linux", ip: "198.51.100.9"));

        var item = Assert.Single(await registry.ListAsync(userId));
        Assert.Equal(sessionId, item.SessionId);
        Assert.Equal("Firefox on Linux", item.Device);
        Assert.Equal("198.51.100.9", item.IpAddress);
    }

    [Fact]
    public async Task Sessions_are_scoped_per_user()
    {
        var registry = CreateRegistry();
        var owner = Guid.NewGuid();
        await registry.RecordSeenAsync(Observation(owner, Guid.NewGuid()));

        Assert.Empty(await registry.ListAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Revoking_the_accounts_live_session_returns_true_and_ends_its_access()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await registry.RecordSeenAsync(Observation(userId, sessionId));

        var revoked = await registry.RevokeAsync(userId, sessionId);

        Assert.True(revoked);
        Assert.Empty(await registry.ListAsync(userId));
        Assert.True(await registry.IsRevokedAsync(userId, sessionId));
    }

    [Fact]
    public async Task Revoking_another_accounts_session_returns_false_and_leaves_it_live()
    {
        var registry = CreateRegistry();
        var owner = Guid.NewGuid();
        var caller = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await registry.RecordSeenAsync(Observation(owner, sessionId));

        Assert.False(await registry.RevokeAsync(caller, sessionId));

        Assert.Equal(sessionId, Assert.Single(await registry.ListAsync(owner)).SessionId);
        Assert.False(await registry.IsRevokedAsync(owner, sessionId));
        Assert.False(await registry.IsRevokedAsync(caller, sessionId));
    }

    [Fact]
    public async Task Revoking_an_unknown_session_returns_false_without_a_tombstone()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var liveSessionId = Guid.NewGuid();
        await registry.RecordSeenAsync(Observation(userId, liveSessionId));

        Assert.False(await registry.RevokeAsync(userId, sessionId));
        Assert.False(await registry.IsRevokedAsync(userId, sessionId));
        Assert.Equal(liveSessionId, Assert.Single(await registry.ListAsync(userId)).SessionId);
    }

    [Fact]
    public async Task Revoking_an_already_revoked_session_returns_false()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await registry.RecordSeenAsync(Observation(userId, sessionId));
        Assert.True(await registry.RevokeAsync(userId, sessionId));

        Assert.False(await registry.RevokeAsync(userId, sessionId));
        Assert.True(await registry.IsRevokedAsync(userId, sessionId));
    }

    [Fact]
    public async Task Revoking_an_expired_session_returns_false_without_a_tombstone()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await registry.RecordSeenAsync(Observation(userId, sessionId, expires: DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.False(await registry.RevokeAsync(userId, sessionId));
        Assert.False(await registry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await registry.ListAsync(userId));
    }

    [Fact]
    public async Task Competing_revocations_report_exactly_one_live_session_removed()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await registry.RecordSeenAsync(Observation(userId, sessionId));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Task.Run(async () =>
        {
            await start.Task;
            return await registry.RevokeAsync(userId, sessionId);
        });
        var second = Task.Run(async () =>
        {
            await start.Task;
            return await registry.RevokeAsync(userId, sessionId);
        });

        start.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, removed => removed);
        Assert.Single(results, removed => !removed);
        Assert.True(await registry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await registry.ListAsync(userId));
    }

    [Fact]
    public async Task Recording_during_revocation_cannot_restore_access()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var observation = Observation(userId, sessionId);
        await registry.RecordSeenAsync(observation);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var record = Task.Run(async () =>
        {
            await start.Task;
            await registry.RecordSeenAsync(observation);
        });
        var revoke = Task.Run(async () =>
        {
            await start.Task;
            return await registry.RevokeAsync(userId, sessionId);
        });

        start.SetResult();
        await record;

        Assert.True(await revoke);
        Assert.True(await registry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await registry.ListAsync(userId));
    }

    [Fact]
    public async Task A_revoked_session_cannot_resurrect_itself()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await registry.RecordSeenAsync(Observation(userId, sessionId));
        await registry.RevokeAsync(userId, sessionId);

        await registry.RecordSeenAsync(Observation(userId, sessionId));

        Assert.Empty(await registry.ListAsync(userId));
        Assert.True(await registry.IsRevokedAsync(userId, sessionId));
    }

    [Fact]
    public async Task An_expired_session_is_not_listed()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        await registry.RecordSeenAsync(Observation(userId, Guid.NewGuid(), expires: DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Empty(await registry.ListAsync(userId));
    }

    [Fact]
    public async Task Sessions_list_most_recently_active_first()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();

        await registry.RecordSeenAsync(Observation(userId, older, seen: now.AddMinutes(-10), expires: now.AddHours(1)));
        await registry.RecordSeenAsync(Observation(userId, newer, seen: now.AddMinutes(-1), expires: now.AddHours(1)));

        var list = await registry.ListAsync(userId);
        Assert.Equal(newer, list[0].SessionId);
        Assert.Equal(older, list[1].SessionId);
    }

    [Fact]
    public async Task Last_seen_stays_unchanged_within_the_throttle_window()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var firstSeen = DateTimeOffset.UtcNow;

        await registry.RecordSeenAsync(Observation(userId, sessionId, seen: firstSeen, expires: firstSeen.AddHours(1)));
        await registry.RecordSeenAsync(Observation(userId, sessionId, seen: firstSeen.AddSeconds(30), expires: firstSeen.AddHours(1)));

        var afterThrottled = Assert.Single(await registry.ListAsync(userId));
        Assert.Equal(firstSeen, afterThrottled.LastSeenAtUtc);
    }

    [Fact]
    public async Task Refreshing_last_seen_preserves_created_at_after_the_throttle_window()
    {
        var registry = CreateRegistry();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var firstSeen = DateTimeOffset.UtcNow;
        await registry.RecordSeenAsync(Observation(userId, sessionId, seen: firstSeen, expires: firstSeen.AddHours(1)));

        await registry.RecordSeenAsync(Observation(userId, sessionId, seen: firstSeen.AddSeconds(90), expires: firstSeen.AddHours(1)));

        var afterMoved = Assert.Single(await registry.ListAsync(userId));
        Assert.Equal(firstSeen.AddSeconds(90), afterMoved.LastSeenAtUtc);
        Assert.Equal(firstSeen, afterMoved.CreatedAtUtc);
    }

    [Fact]
    public async Task An_unknown_session_is_not_revoked()
        => Assert.False(await CreateRegistry().IsRevokedAsync(Guid.NewGuid(), Guid.NewGuid()));
}
