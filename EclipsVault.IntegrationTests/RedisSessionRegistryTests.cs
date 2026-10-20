using EclipsVault.Core.Application.Sessions;
using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class RedisSessionRegistryTests(RedisSessionFixture fixture) : IClassFixture<RedisSessionFixture>
{
    private static SessionObservation Observation(Guid userId, Guid sessionId, DateTimeOffset? expires = null)
        => new(userId, sessionId, "Chrome on macOS", "203.0.113.5", DateTimeOffset.UtcNow,
            expires ?? DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public async Task Revoking_the_accounts_live_session_returns_true_and_ends_access_on_another_node()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, sessionId));
        Assert.Equal(sessionId, Assert.Single(await fixture.PeerRegistry.ListAsync(userId)).SessionId);

        Assert.True(await fixture.Registry.RevokeAsync(userId, sessionId));

        Assert.True(await fixture.PeerRegistry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await fixture.PeerRegistry.ListAsync(userId));
    }

    [Fact]
    public async Task Revoking_another_accounts_session_returns_false_and_leaves_it_live()
    {
        var owner = Guid.NewGuid();
        var caller = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(owner, sessionId));

        Assert.False(await fixture.PeerRegistry.RevokeAsync(caller, sessionId));

        Assert.Equal(sessionId, Assert.Single(await fixture.Registry.ListAsync(owner)).SessionId);
        Assert.False(await fixture.Registry.IsRevokedAsync(owner, sessionId));
        Assert.False(await fixture.Registry.IsRevokedAsync(caller, sessionId));
    }

    [Fact]
    public async Task Revoking_an_unknown_session_returns_false_without_a_tombstone()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var liveSessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, liveSessionId));

        Assert.False(await fixture.Registry.RevokeAsync(userId, sessionId));
        Assert.False(await fixture.PeerRegistry.IsRevokedAsync(userId, sessionId));
        Assert.Equal(liveSessionId, Assert.Single(await fixture.Registry.ListAsync(userId)).SessionId);
    }

    [Fact]
    public async Task Revoking_an_already_revoked_session_returns_false()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, sessionId));
        Assert.True(await fixture.Registry.RevokeAsync(userId, sessionId));

        Assert.False(await fixture.PeerRegistry.RevokeAsync(userId, sessionId));
        Assert.True(await fixture.Registry.IsRevokedAsync(userId, sessionId));
    }

    [Fact]
    public async Task Revoking_an_expired_session_returns_false_without_a_tombstone()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, sessionId, DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.False(await fixture.PeerRegistry.RevokeAsync(userId, sessionId));
        Assert.False(await fixture.Registry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await fixture.Registry.ListAsync(userId));
    }

    [Fact]
    public async Task Competing_nodes_report_exactly_one_live_session_removed()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, sessionId));

        var first = fixture.Registry.RevokeAsync(userId, sessionId);
        var second = fixture.PeerRegistry.RevokeAsync(userId, sessionId);
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, removed => removed);
        Assert.Single(results, removed => !removed);
        Assert.True(await fixture.PeerRegistry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await fixture.Registry.ListAsync(userId));
    }

    [Fact]
    public async Task Recording_on_another_node_during_revocation_cannot_restore_access()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var observation = Observation(userId, sessionId);
        await fixture.Registry.RecordSeenAsync(observation);

        var revoke = fixture.Registry.RevokeAsync(userId, sessionId);
        var record = fixture.PeerRegistry.RecordSeenAsync(observation);
        await record;
        Assert.True(await revoke);
        await fixture.PeerRegistry.RecordSeenAsync(observation);

        Assert.True(await fixture.PeerRegistry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await fixture.PeerRegistry.ListAsync(userId));
    }
}
