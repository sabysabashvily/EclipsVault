using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Distributed;
using EclipsVault.Infrastructure.Security.WebAuthn;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class RedisPasskeyCeremonyTests(RedisSessionFixture fixture) : IClassFixture<RedisSessionFixture>
{
    [Fact]
    public async Task Two_nodes_cannot_complete_the_same_ceremony_twice()
    {
        await using var node = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        await using var peer = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        var options = Options.Create(new RedisOptions { InstanceName = "ceremony-tests" });
        var store = new RedisPasskeyCeremonyStore(node, options);
        var other = new RedisPasskeyCeremonyStore(peer, options);
        await store.ReplaceAsync("browser", PasskeyCeremonyPurpose.SignIn, "state", default);
        var ttl = await fixture.Database.KeyTimeToLiveAsync($"{options.Value.SessionKeyPrefix}:passkey-ceremony:browser:SignIn");

        var taken = await Task.WhenAll(store.TakeAsync("browser", PasskeyCeremonyPurpose.SignIn, default),
            other.TakeAsync("browser", PasskeyCeremonyPurpose.SignIn, default));

        Assert.Single(taken, value => value == "state");
        Assert.Single(taken, value => value is null);
        Assert.InRange(ttl!.Value.TotalSeconds, 110, 120);
    }

    [Fact]
    public async Task Replacing_a_ceremony_cancels_its_previous_state()
    {
        await using var node = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        var store = new RedisPasskeyCeremonyStore(node, Options.Create(new RedisOptions { InstanceName = "ceremony-replacement" }));
        await store.ReplaceAsync("browser", PasskeyCeremonyPurpose.Registration, "old", default);

        await store.ReplaceAsync("browser", PasskeyCeremonyPurpose.Registration, "new", default);

        Assert.Equal("new", await store.TakeAsync("browser", PasskeyCeremonyPurpose.Registration, default));
    }

    [Fact]
    public async Task A_ceremony_from_an_earlier_incarnation_cannot_be_completed()
    {
        await using var node = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        var store = new RedisPasskeyCeremonyStore(node, Options.Create(new RedisOptions { InstanceName = "ceremony-incarnation" }));
        var rotated = new RedisPasskeyCeremonyStore(node, Options.Create(new RedisOptions
        {
            InstanceName = "ceremony-incarnation",
            SessionIncarnation = "new",
        }));
        await store.ReplaceAsync("browser", PasskeyCeremonyPurpose.Registration, "state", default);

        Assert.Null(await rotated.TakeAsync("browser", PasskeyCeremonyPurpose.Registration, default));
        Assert.Equal("state", await store.TakeAsync("browser", PasskeyCeremonyPurpose.Registration, default));
    }

    [Fact]
    public async Task Expired_ceremony_state_cannot_be_taken()
    {
        await using var node = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        var options = Options.Create(new RedisOptions { InstanceName = "ceremony-expiry" });
        var store = new RedisPasskeyCeremonyStore(node, options);
        await store.ReplaceAsync("browser", PasskeyCeremonyPurpose.Registration, "expired", default);
        await fixture.Database.KeyExpireAsync($"{options.Value.SessionKeyPrefix}:passkey-ceremony:browser:Registration", TimeSpan.Zero);

        Assert.Null(await store.TakeAsync("browser", PasskeyCeremonyPurpose.Registration, default));
    }
}
