using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Networks;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Distributed;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.Tests.Distributed;

public sealed class BlockAdministrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_successful_lift_records_one_event_after_removing_the_block(bool redis)
    {
        var blacklist = Blacklist(redis);
        await blacklist.BlockAsync("198.51.100.113", "original reason");
        var audit = new RecordingAudit(blacklist);
        var administration = new BlockAdministration(blacklist, audit);

        Assert.True(await administration.LiftAsync(" 198.51.100.113/32 "));

        Assert.False(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.113")));
        Assert.False(audit.BlockedDuringWrite);
        Assert.Equal(new AuditEntry
        {
            Action = AuditAction.IpRangeUnblocked,
            ResourceType = "TrustedNetwork",
            ResourceName = "198.51.100.113/32",
            Details = "Intrusion-defence block lifted"
        }, Assert.Single(audit.Recorded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_audit_restores_the_exact_original_record(bool redis)
    {
        var blacklist = Blacklist(redis);
        await blacklist.BlockAsync("198.51.100.113", "original reason");
        var original = Assert.Single(await blacklist.ListAsync());
        var audit = new RecordingAudit(blacklist) { RejectWrites = true };
        var administration = new BlockAdministration(blacklist, audit);

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => administration.LiftAsync("198.51.100.113/32"));

        Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.113")));
        Assert.Equal(original, Assert.Single(await blacklist.ListAsync()));
        Assert.False(audit.BlockedDuringWrite);
        Assert.Empty(audit.Recorded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_restore_reports_the_unrecorded_lift_without_overwriting_a_new_block(bool redis)
    {
        var blacklist = Blacklist(redis);
        await blacklist.BlockAsync("198.51.100.113", "original reason");
        var audit = new RecordingAudit(blacklist)
        {
            RejectWrites = true,
            BeforeWrite = () => blacklist.BlockAsync("198.51.100.113", "new containment")
        };
        var administration = new BlockAdministration(blacklist, audit);

        var failure = await Assert.ThrowsAsync<UnblockUnrecordedException>(() => administration.LiftAsync("198.51.100.113/32"));

        Assert.Equal("The block was lifted, but recording and restoring it failed. Check the range before retrying.", failure.Message);
        Assert.Equal("new containment", Assert.Single(await blacklist.ListAsync()).Reason);
        Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.113")));
        Assert.Empty(audit.Recorded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifting_an_absent_range_keeps_other_blocks_and_records_nothing(bool redis)
    {
        var blacklist = Blacklist(redis);
        await blacklist.BlockAsync("198.51.100.113", "original reason");
        var original = Assert.Single(await blacklist.ListAsync());
        var audit = new RecordingAudit(blacklist);
        var administration = new BlockAdministration(blacklist, audit);

        Assert.False(await administration.LiftAsync("198.51.100.114/32"));

        Assert.Equal(original, Assert.Single(await blacklist.ListAsync()));
        Assert.Empty(audit.Recorded);
        Assert.Equal(0, audit.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_disconnected_browser_cannot_cancel_compensation(bool redis)
    {
        using var request = new CancellationTokenSource();
        var blacklist = Blacklist(redis);
        await blacklist.BlockAsync("198.51.100.113", "original reason");
        var original = Assert.Single(await blacklist.ListAsync());
        var audit = new RecordingAudit(blacklist)
        {
            RejectWrites = true,
            BeforeWrite = () => { request.Cancel(); return Task.CompletedTask; }
        };
        var administration = new BlockAdministration(blacklist, audit);

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => administration.LiftAsync("198.51.100.113/32", request.Token));

        Assert.Equal(original, Assert.Single(await blacklist.ListAsync()));
        Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.113")));
        Assert.False(audit.WriteToken.CanBeCanceled);
        Assert.Empty(audit.Recorded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_disconnected_browser_still_waits_for_the_committed_lift(bool redis)
    {
        using var request = new CancellationTokenSource();
        var blacklist = Blacklist(redis);
        await blacklist.BlockAsync("198.51.100.113", "original reason");
        var audit = new RecordingAudit(blacklist)
        {
            BeforeWrite = () => { request.Cancel(); return Task.CompletedTask; }
        };
        var administration = new BlockAdministration(blacklist, audit);

        Assert.True(await administration.LiftAsync("198.51.100.113/32", request.Token));

        Assert.False(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.113")));
        Assert.Equal(AuditAction.IpRangeUnblocked, Assert.Single(audit.Recorded).Action);
        Assert.False(audit.WriteToken.CanBeCanceled);
    }

    private static IIpBlacklist Blacklist(bool redis) => redis
        ? new RedisIpBlacklist(new RedisBlacklistStore().Connection,
            Options.Create(new RedisOptions { InstanceName = "unblock-unit" }),
            Options.Create(new IntrusionResponseOptions()), NullLogger<RedisIpBlacklist>.Instance)
        : new InMemoryIpBlacklist(new AdvancingClock(), Options.Create(new IntrusionResponseOptions()),
            NullLogger<InMemoryIpBlacklist>.Instance);

    private sealed class AdvancingClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 1, 12, 30, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            var now = _now;
            _now = _now.AddDays(1);
            return now;
        }
    }

    private sealed class RecordingAudit(IIpBlacklist blacklist) : IAuditSink
    {
        public List<AuditEntry> Recorded { get; } = [];
        public bool RejectWrites { get; init; }
        public Func<Task>? BeforeWrite { get; init; }
        public bool BlockedDuringWrite { get; private set; }
        public CancellationToken WriteToken { get; private set; }
        public int Attempts { get; private set; }

        public async Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            Attempts++;
            WriteToken = ct;
            BlockedDuringWrite = await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.113"));
            if (BeforeWrite is not null)
            {
                await BeforeWrite();
            }

            ct.ThrowIfCancellationRequested();
            if (RejectWrites)
            {
                throw new AuditWriteFailedException("Audit insert refused");
            }

            Recorded.Add(entry);
        }
    }
}
