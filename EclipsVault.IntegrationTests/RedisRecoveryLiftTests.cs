using System.Net;
using EclipsVault.Infrastructure.Distributed;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Break-glass's lift on the shared Redis blacklist, seen from two replicas (ADR-0032). The guard is
/// in place before any block goes, every replica enforces it, no lift of a block removes it, and it
/// does not expire: only settling the lift releases it. <c>BreakGlassSignInTests</c> drives the same
/// lift through break-glass on the in-process blacklist, and <c>AuthAuditFailureTests</c> through HTTP.
/// </summary>
public sealed class RedisRecoveryLiftTests(RedisSessionFixture fixture) : IClassFixture<RedisSessionFixture>
{
    private static readonly IPAddress Source = IPAddress.Parse("198.51.100.120");

    private const string Host = """{ "Reason" : "Honey-token 'Host' tripped", "BlockedAtUtc" : "2026-10-01T15:30:00+03:00" }""";
    private const string Range = """{ "Reason" : "Honey-token 'Range' tripped", "BlockedAtUtc" : "2026-10-02T09:00:00+00:00" }""";

    [Fact]
    public async Task Every_replica_refuses_the_source_until_the_lift_is_settled_whatever_else_is_lifted()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "lift-guard");
        await fixture.Database.StringSetAsync("lift-guard:ipblock:198.51.100.120/32", Host);

        var lift = await replicas.First.LiftForRecoveryAsync(Source);

        Assert.True(lift.Lifted);
        Assert.Empty(await replicas.Second.ListAsync());
        Assert.True(await replicas.Second.IsBlockedAsync(Source));
        Assert.Null(await replicas.Second.UnblockAsync("198.51.100.120/32"));
        await (await replicas.Second.LiftForRecoveryAsync(Source)).ReleaseAsync();
        Assert.True(await replicas.Second.IsBlockedAsync(Source));
        Assert.Null(await fixture.Database.KeyTimeToLiveAsync("lift-guard:ipguard:198.51.100.120/32"));

        await lift.ReleaseAsync();

        Assert.False(await replicas.Second.IsBlockedAsync(Source));
        Assert.False(await fixture.Database.KeyExistsAsync("lift-guard:ipguard:198.51.100.120/32"));
    }

    [Fact]
    public async Task A_restoration_puts_back_both_overlapping_records_byte_for_byte()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "lift-overlap");
        // Replicas configured for different block sizes keyed the address both ways.
        await fixture.Database.StringSetAsync("lift-overlap:ipblock:198.51.100.120/32", Host);
        await fixture.Database.StringSetAsync("lift-overlap:ipblock:198.51.100.0/24", Range);

        var lift = await replicas.First.LiftForRecoveryAsync(Source);
        Assert.False(await fixture.Database.KeyExistsAsync("lift-overlap:ipblock:198.51.100.120/32"));
        Assert.False(await fixture.Database.KeyExistsAsync("lift-overlap:ipblock:198.51.100.0/24"));

        Assert.True(await lift.TryRestoreAsync());

        Assert.Equal(Host, (string?)await fixture.Database.StringGetAsync("lift-overlap:ipblock:198.51.100.120/32"));
        Assert.Equal(Range, (string?)await fixture.Database.StringGetAsync("lift-overlap:ipblock:198.51.100.0/24"));
        Assert.True(await replicas.Second.IsBlockedAsync(Source));
        Assert.False(await fixture.Database.KeyExistsAsync("lift-overlap:ipguard:198.51.100.120/32"));
    }

    [Fact]
    public async Task Blocks_raised_elsewhere_meanwhile_survive_the_restoration()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "lift-concurrent");
        await fixture.Database.StringSetAsync("lift-concurrent:ipblock:198.51.100.120/32", Host);
        var lift = await replicas.First.LiftForRecoveryAsync(Source);

        await replicas.Second.BlockAsync("198.51.100.121", "raised meanwhile");

        Assert.True(await lift.TryRestoreAsync());
        Assert.Equal(Host, (string?)await fixture.Database.StringGetAsync("lift-concurrent:ipblock:198.51.100.120/32"));
        Assert.Equal("raised meanwhile", Assert.Single(await replicas.Second.ListAsync(), block => block.Network == "198.51.100.121/32").Reason);
    }

    [Fact]
    public async Task A_block_raised_on_the_same_range_meanwhile_is_kept_and_the_source_stays_guarded()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "lift-same-range");
        await fixture.Database.StringSetAsync("lift-same-range:ipblock:198.51.100.120/32", Host);
        var lift = await replicas.First.LiftForRecoveryAsync(Source);
        await replicas.Second.BlockAsync(Source.ToString(), "raised meanwhile");

        Assert.False(await lift.TryRestoreAsync());

        Assert.Equal("raised meanwhile", Assert.Single(await replicas.Second.ListAsync()).Reason);
        Assert.NotNull(await replicas.Second.UnblockAsync("198.51.100.120/32"));
        Assert.True(await replicas.Second.IsBlockedAsync(Source));
    }

    [Fact]
    public async Task A_restoration_that_loses_redis_is_unconfirmed_and_another_replica_still_refuses_the_source()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "lift-lost");
        await fixture.Database.StringSetAsync("lift-lost:ipblock:198.51.100.120/32", Host);
        var lift = await replicas.First.LiftForRecoveryAsync(Source);

        await replicas.CloseFirstAsync();

        Assert.False(await lift.TryRestoreAsync());
        Assert.False(await fixture.Database.KeyExistsAsync("lift-lost:ipblock:198.51.100.120/32"));
        Assert.True(await replicas.Second.IsBlockedAsync(Source));
    }

    private sealed class Replicas : IAsyncDisposable
    {
        private readonly ConnectionMultiplexer _first;
        private readonly ConnectionMultiplexer _second;

        private Replicas(ConnectionMultiplexer first, ConnectionMultiplexer second, string prefix)
        {
            _first = first;
            _second = second;
            First = Blacklist(first, prefix);
            Second = Blacklist(second, prefix);
        }

        public RedisIpBlacklist First { get; }
        public RedisIpBlacklist Second { get; }

        public static async Task<Replicas> ConnectAsync(RedisSessionFixture fixture, string prefix)
            => new(await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString),
                await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString), prefix);

        public Task CloseFirstAsync() => _first.CloseAsync();

        public async ValueTask DisposeAsync()
        {
            await _first.DisposeAsync();
            await _second.DisposeAsync();
        }

        private static RedisIpBlacklist Blacklist(IConnectionMultiplexer connection, string prefix)
            => new(connection, Options.Create(new RedisOptions { InstanceName = prefix }),
                Options.Create(new IntrusionResponseOptions()), NullLogger<RedisIpBlacklist>.Instance);
    }
}
