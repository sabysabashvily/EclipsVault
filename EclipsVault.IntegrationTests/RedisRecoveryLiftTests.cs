using System.Net;
using EclipsVault.Core.Application.Abstractions;
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

    [Fact]
    public async Task A_stuck_lift_is_listed_on_every_replica_and_a_live_one_is_not()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "stuck-list");
        await (await replicas.First.LiftForRecoveryAsync(Source)).MarkStuckAsync();
        await replicas.First.LiftForRecoveryAsync(IPAddress.Parse("198.51.100.130"));

        Assert.Equal(["198.51.100.0/24", "198.51.100.120/32"], await replicas.Second.ListStuckGuardsAsync());
    }

    [Fact]
    public async Task A_claimed_guard_keeps_refusing_on_every_replica_until_it_is_released()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "stuck-claim");
        await (await replicas.First.LiftForRecoveryAsync(Source)).MarkStuckAsync();

        var claimed = await replicas.Second.ClaimStuckGuardAsync("198.51.100.120/32");

        Assert.True(await replicas.First.IsBlockedAsync(Source));
        await claimed!.ReleaseAsync();
        Assert.False(await replicas.First.IsBlockedAsync(Source));
    }

    [Fact]
    public async Task A_second_replica_cannot_claim_a_guard_another_release_holds()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "stuck-twice");
        await (await replicas.First.LiftForRecoveryAsync(Source)).MarkStuckAsync();
        Assert.NotNull(await replicas.First.ClaimStuckGuardAsync("198.51.100.120/32"));

        Assert.Null(await replicas.Second.ClaimStuckGuardAsync("198.51.100.120/32"));
    }

    [Fact]
    public async Task Two_replicas_claiming_the_same_range_cannot_split_its_stuck_guards()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "stuck-race");
        await (await replicas.First.LiftForRecoveryAsync(Source)).MarkStuckAsync();
        await (await replicas.Second.LiftForRecoveryAsync(Source)).MarkStuckAsync();

        var claims = await Task.WhenAll(replicas.First.ClaimStuckGuardAsync("198.51.100.120/32"),
            replicas.Second.ClaimStuckGuardAsync("198.51.100.120/32"));

        await Assert.Single(claims.OfType<IClaimedGuard>()).ReleaseAsync();
        Assert.False(await replicas.Second.IsBlockedAsync(Source));
    }

    [Fact]
    public async Task A_stale_mark_after_a_guard_was_removed_cannot_be_listed_or_claimed()
    {
        const string range = "198.51.100.120/32";
        await using var replicas = await Replicas.ConnectAsync(fixture, "stuck-stale");
        await (await replicas.First.LiftForRecoveryAsync(Source)).MarkStuckAsync();
        var members = await fixture.Database.SetMembersAsync("stuck-stale:ipstuck:198.51.100.120/32");
        var claim = await replicas.First.ClaimStuckGuardAsync(range);
        await claim!.ReleaseAsync();
        await replicas.Second.LiftForRecoveryAsync(Source);
        // An older replica could restore the mark if Redis applied the release but lost its reply.
        await fixture.Database.SetAddAsync("stuck-stale:ipstuck:198.51.100.120/32", members);

        Assert.DoesNotContain(range, await replicas.Second.ListStuckGuardsAsync());
        Assert.Null(await replicas.Second.ClaimStuckGuardAsync(range));
    }

    [Fact]
    public async Task Marking_a_settled_lift_stuck_does_not_create_a_redis_mark()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "stuck-settled");
        var lift = await replicas.First.LiftForRecoveryAsync(Source);
        await lift.ReleaseAsync();
        await replicas.Second.LiftForRecoveryAsync(Source);

        await lift.MarkStuckAsync();

        Assert.False(await fixture.Database.KeyExistsAsync("stuck-settled:ipstuck:198.51.100.120/32"));
    }

    [Fact]
    public async Task A_guard_whose_audited_removal_cannot_run_still_refuses_on_another_replica()
    {
        const string range = "198.51.100.120/32";
        await using var replicas = await Replicas.ConnectAsync(fixture, "stuck-release-offline");
        await (await replicas.First.LiftForRecoveryAsync(Source)).MarkStuckAsync();
        var claim = await replicas.First.ClaimStuckGuardAsync(range);
        Assert.NotNull(claim);

        await replicas.CloseFirstAsync();
        Assert.NotNull(await Record.ExceptionAsync(claim.ReleaseAsync));

        Assert.True(await replicas.Second.IsBlockedAsync(Source));
        Assert.Null(await replicas.Second.ClaimStuckGuardAsync(range));
    }

    [Fact]
    public async Task An_abandoned_claim_lists_the_guard_again_and_it_still_refuses()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "stuck-abandon");
        await (await replicas.First.LiftForRecoveryAsync(Source)).MarkStuckAsync();

        await (await replicas.Second.ClaimStuckGuardAsync("198.51.100.120/32"))!.AbandonAsync();

        Assert.Contains("198.51.100.120/32", await replicas.First.ListStuckGuardsAsync());
        Assert.True(await replicas.First.IsBlockedAsync(Source));
    }

    [Fact]
    public async Task A_release_leaves_the_guard_of_a_live_recovery_on_the_same_range()
    {
        await using var replicas = await Replicas.ConnectAsync(fixture, "stuck-live");
        await (await replicas.First.LiftForRecoveryAsync(Source)).MarkStuckAsync();
        var live = await replicas.First.LiftForRecoveryAsync(Source);

        await (await replicas.Second.ClaimStuckGuardAsync("198.51.100.120/32"))!.ReleaseAsync();

        Assert.True(await replicas.Second.IsBlockedAsync(Source));
        await live.ReleaseAsync();
        Assert.False(await replicas.Second.IsBlockedAsync(Source));
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
