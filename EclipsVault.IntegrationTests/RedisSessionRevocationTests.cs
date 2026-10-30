using EclipsVault.Core.Application.Sessions;
using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class RedisSessionRevocationTests(RedisSessionFixture fixture) : IClassFixture<RedisSessionFixture>
{
    [Fact]
    public async Task Concurrent_first_sign_ins_on_two_nodes_capture_one_version()
    {
        var accountId = Guid.NewGuid();

        var versions = await Task.WhenAll(
            fixture.Revocation.CaptureAsync(accountId), fixture.PeerRevocation.CaptureAsync(accountId));

        Assert.Equal(versions[0], versions[1]);
        Assert.Equal(versions[0], await fixture.PeerRevocation.FindAsync(accountId));
    }

    [Fact]
    public async Task Two_immediate_sign_outs_retire_the_version_captured_between_them()
    {
        var accountId = Guid.NewGuid();
        await fixture.Revocation.RevokeAsync(accountId);
        var between = await fixture.PeerRevocation.CaptureAsync(accountId);

        await fixture.PeerRevocation.RevokeAsync(accountId);

        Assert.NotEqual(between, await fixture.Revocation.FindAsync(accountId));
    }

    [Fact]
    public async Task Concurrent_sign_outs_on_two_nodes_retire_the_original_version()
    {
        var accountId = Guid.NewGuid();
        var original = await fixture.Revocation.CaptureAsync(accountId);

        await Task.WhenAll(fixture.Revocation.RevokeAsync(accountId), fixture.PeerRevocation.RevokeAsync(accountId));

        Assert.NotEqual(original, await fixture.Revocation.FindAsync(accountId));
        Assert.Equal(await fixture.Revocation.FindAsync(accountId), await fixture.PeerRevocation.FindAsync(accountId));
    }

    [Fact]
    public async Task Missing_state_never_matches_an_old_version_or_initializes_itself_on_read()
    {
        var accountId = Guid.NewGuid();
        var original = await fixture.Revocation.CaptureAsync(accountId);
        await fixture.Database.KeyDeleteAsync(VersionKey(accountId));

        Assert.Null(await fixture.PeerRevocation.FindAsync(accountId));
        Assert.False(await fixture.Database.KeyExistsAsync(VersionKey(accountId)));
        Assert.NotEqual(original, await fixture.PeerRevocation.CaptureAsync(accountId));
    }

    [Fact]
    public async Task Expired_state_is_refused_and_a_later_sign_in_gets_a_distinct_version()
    {
        var accountId = Guid.NewGuid();
        var original = await fixture.Revocation.CaptureAsync(accountId);
        await fixture.Database.KeyExpireAsync(VersionKey(accountId), DateTime.UtcNow.AddMinutes(-1));

        Assert.Null(await fixture.Revocation.FindAsync(accountId));
        Assert.NotEqual(original, await fixture.PeerRevocation.CaptureAsync(accountId));
    }

    [Fact]
    public async Task A_late_observation_cannot_restore_a_device_from_a_retired_generation()
    {
        var accountId = Guid.NewGuid();
        var version = await fixture.Revocation.CaptureAsync(accountId);
        var original = new SessionStamp(3, version);
        var current = original with { CredentialGeneration = 4 };
        var observation = new SessionObservation(accountId, Guid.NewGuid(), original, "Firefox", "192.0.2.20",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));

        await fixture.Registry.RecordSeenAsync(observation);
        await fixture.PeerRegistry.RecordSeenAsync(observation);

        Assert.Empty(await fixture.Registry.ListAsync(accountId, current));
    }

    [Fact]
    public async Task A_late_observation_cannot_restore_a_device_after_account_wide_sign_out()
    {
        var accountId = Guid.NewGuid();
        var original = new SessionStamp(0, await fixture.Revocation.CaptureAsync(accountId));
        var observation = new SessionObservation(accountId, Guid.NewGuid(), original, "Firefox", "192.0.2.21",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        await fixture.Registry.RecordSeenAsync(observation);
        await fixture.Revocation.RevokeAsync(accountId);
        var current = original with { RevocationVersion = (await fixture.PeerRevocation.FindAsync(accountId))! };

        await fixture.PeerRegistry.RecordSeenAsync(observation);

        Assert.Empty(await fixture.Registry.ListAsync(accountId, current));
    }

    private string VersionKey(Guid accountId) => $"{fixture.SessionPrefix}:revocation-version:{accountId:N}";
}
