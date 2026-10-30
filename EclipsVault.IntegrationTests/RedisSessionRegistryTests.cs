using EclipsVault.Core.Application.Sessions;
using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class RedisSessionRegistryTests(RedisSessionFixture fixture) : IClassFixture<RedisSessionFixture>
{
    private static readonly SessionStamp Stamp = new(0, "current-version");

    private static SessionObservation Observation(Guid userId, Guid sessionId, DateTimeOffset? expires = null)
        => new(userId, sessionId, Stamp, "Chrome on macOS", "203.0.113.5", DateTimeOffset.UtcNow,
            expires ?? DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public async Task A_device_sign_out_winning_before_replacement_prevents_retirement()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, sessionId));
        Assert.True(await fixture.PeerRegistry.RevokeAsync(userId, sessionId));

        Assert.False(await fixture.Registry.TryRetireForReplacementAsync(userId, sessionId));
    }

    [Fact]
    public async Task Retirement_winning_before_a_device_sign_out_cannot_be_reported_as_a_confirmed_removal()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, sessionId));
        Assert.True(await fixture.Registry.TryRetireForReplacementAsync(userId, sessionId));

        Assert.False(await fixture.PeerRegistry.RevokeAsync(userId, sessionId));
        Assert.True(await fixture.PeerRegistry.IsRevokedAsync(userId, sessionId));
    }

    [Fact]
    public async Task Concurrent_retirement_and_device_sign_out_have_exactly_one_winner()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, sessionId));

        var outcomes = await Task.WhenAll(
            fixture.Registry.TryRetireForReplacementAsync(userId, sessionId), fixture.PeerRegistry.RevokeAsync(userId, sessionId));

        Assert.NotEqual(outcomes[0], outcomes[1]);
        Assert.True(await fixture.PeerRegistry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await fixture.Registry.ListAsync(userId, Stamp));
    }

    [Fact]
    public async Task Missing_device_metadata_does_not_prevent_retiring_a_valid_session_for_replacement()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        Assert.True(await fixture.Registry.TryRetireForReplacementAsync(userId, sessionId));
        await fixture.PeerRegistry.RecordSeenAsync(Observation(userId, sessionId));

        Assert.True(await fixture.PeerRegistry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await fixture.Registry.ListAsync(userId, Stamp));
    }

    [Fact]
    public async Task Revoking_the_accounts_live_session_returns_true_and_ends_access_on_another_node()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, sessionId));
        Assert.Equal(sessionId, Assert.Single(await fixture.PeerRegistry.ListAsync(userId, Stamp)).SessionId);

        Assert.True(await fixture.Registry.RevokeAsync(userId, sessionId));

        Assert.True(await fixture.PeerRegistry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await fixture.PeerRegistry.ListAsync(userId, Stamp));
    }

    [Fact]
    public async Task Revoking_another_accounts_session_returns_false_and_leaves_it_live()
    {
        var owner = Guid.NewGuid();
        var caller = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(owner, sessionId));

        Assert.False(await fixture.PeerRegistry.RevokeAsync(caller, sessionId));

        Assert.Equal(sessionId, Assert.Single(await fixture.Registry.ListAsync(owner, Stamp)).SessionId);
        Assert.False(await fixture.Registry.IsRevokedAsync(owner, sessionId));
    }

    [Fact]
    public async Task Revoking_an_unknown_session_returns_false_and_leaves_live_sessions_untouched()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var liveSessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, liveSessionId));

        Assert.False(await fixture.Registry.RevokeAsync(userId, sessionId));
        Assert.Equal(liveSessionId, Assert.Single(await fixture.Registry.ListAsync(userId, Stamp)).SessionId);
    }

    [Fact]
    public async Task Revoking_an_unrecorded_session_prevents_its_cookie_from_creating_a_session()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        Assert.False(await fixture.Registry.RevokeAsync(userId, sessionId));
        await fixture.PeerRegistry.RecordSeenAsync(Observation(userId, sessionId));

        Assert.True(await fixture.PeerRegistry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await fixture.PeerRegistry.ListAsync(userId, Stamp));
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
    public async Task An_interrupted_revocation_is_not_reported_as_a_new_revocation()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, sessionId));
        await fixture.InterruptRevocationAfterTombstoneAsync(userId, sessionId);
        Assert.True(await fixture.PeerRegistry.IsRevokedAsync(userId, sessionId));

        Assert.False(await fixture.PeerRegistry.RevokeAsync(userId, sessionId));

        Assert.True(await fixture.Registry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await fixture.Registry.ListAsync(userId, Stamp));
    }

    [Fact]
    public async Task Revoking_expired_metadata_returns_false_and_keeps_the_cookie_revoked()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await fixture.Registry.RecordSeenAsync(Observation(userId, sessionId, DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.False(await fixture.PeerRegistry.RevokeAsync(userId, sessionId));
        await fixture.Registry.RecordSeenAsync(Observation(userId, sessionId));

        Assert.True(await fixture.Registry.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await fixture.Registry.ListAsync(userId, Stamp));
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
        Assert.Empty(await fixture.Registry.ListAsync(userId, Stamp));
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
        Assert.Empty(await fixture.PeerRegistry.ListAsync(userId, Stamp));
    }
}
