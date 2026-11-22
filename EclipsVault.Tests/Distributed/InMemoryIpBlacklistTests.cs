using System.Net;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.Tests.Distributed;

/// <summary>
/// The blacklist contract shared by the in-process and Redis stores. By default a block contains
/// only the exact offending host — so one trip cannot deny the vault to a whole shared-egress
/// subnet; with range blocking enabled it contains the surrounding /24 (or /64). Blocks are
/// enumerable and liftable, and break-glass recovery lifts exactly what would keep an admin out,
/// under a guard that refuses the range until the lift is settled.
/// </summary>
public class InMemoryIpBlacklistTests
{
    private static InMemoryIpBlacklist NewBlacklist(bool blockSurroundingRange = false)
        => new(
            TimeProvider.System,
            Options.Create(new IntrusionResponseOptions { BlockSurroundingRange = blockSurroundingRange }),
            NullLogger<InMemoryIpBlacklist>.Instance);

    [Fact]
    public async Task By_default_blocking_an_address_contains_only_that_host()
    {
        var blacklist = NewBlacklist();
        await blacklist.BlockAsync("203.0.113.40", "test");

        Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("203.0.113.40")));
        Assert.False(await blacklist.IsBlockedAsync(IPAddress.Parse("203.0.113.7")));
    }

    [Fact]
    public async Task Range_blocking_contains_the_whole_slash24()
    {
        var blacklist = NewBlacklist(blockSurroundingRange: true);
        await blacklist.BlockAsync("203.0.113.40", "test");

        Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("203.0.113.7")));
        Assert.False(await blacklist.IsBlockedAsync(IPAddress.Parse("203.0.114.7")));
    }

    [Fact]
    public async Task List_reports_the_exact_host_by_default()
    {
        var blacklist = NewBlacklist();
        await blacklist.BlockAsync("198.51.100.9", "honey-token tripped");

        var listed = Assert.Single(await blacklist.ListAsync());
        Assert.Equal("198.51.100.9/32", listed.Network);
        Assert.Equal("honey-token tripped", listed.Reason);
    }

    [Fact]
    public async Task List_reports_the_range_when_range_blocking()
    {
        var blacklist = NewBlacklist(blockSurroundingRange: true);
        await blacklist.BlockAsync("198.51.100.9", "honey-token tripped");

        var listed = Assert.Single(await blacklist.ListAsync());
        Assert.Equal("198.51.100.0/24", listed.Network);
    }

    [Fact]
    public async Task Unblock_by_network_lifts_the_block()
    {
        var blacklist = NewBlacklist();
        await blacklist.BlockAsync("198.51.100.9", "test");

        Assert.NotNull(await blacklist.UnblockAsync("198.51.100.9/32"));
        Assert.False(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.9")));
    }

    [Fact]
    public async Task A_settled_recovery_lift_lifts_the_block_covering_the_caller()
    {
        var blacklist = NewBlacklist();
        await blacklist.BlockAsync("198.51.100.9", "test");

        var lift = await blacklist.LiftForRecoveryAsync(IPAddress.Parse("198.51.100.9"));
        Assert.True(lift.Lifted);
        Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.9")));
        await lift.ReleaseAsync();

        Assert.False(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.9")));
    }

    [Fact]
    public async Task A_recovery_lift_in_range_mode_guards_and_lifts_the_whole_range_covering_the_caller()
    {
        var blacklist = NewBlacklist(blockSurroundingRange: true);
        await blacklist.BlockAsync("198.51.100.9", "test");

        var lift = await blacklist.LiftForRecoveryAsync(IPAddress.Parse("198.51.100.200"));
        Assert.Empty(await blacklist.ListAsync());
        Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.9")));
        await lift.ReleaseAsync();

        Assert.False(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.9")));
    }

    [Fact]
    public async Task Unparseable_source_is_a_no_op()
    {
        var blacklist = NewBlacklist();
        await blacklist.BlockAsync("198.51.100.9", "original reason");
        var original = Assert.Single(await blacklist.ListAsync());

        await blacklist.BlockAsync("not-an-ip", "test");

        Assert.Equal(original, Assert.Single(await blacklist.ListAsync()));
        Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.9")));
    }

    [Fact]
    public async Task Loopback_is_pinned_to_its_exact_host()
    {
        var blacklist = NewBlacklist();
        await blacklist.BlockAsync("127.0.0.1", "test");

        Assert.True(await blacklist.IsBlockedAsync(IPAddress.Loopback));
        var listed = Assert.Single(await blacklist.ListAsync());
        Assert.Equal("127.0.0.1/32", listed.Network);
    }
}
