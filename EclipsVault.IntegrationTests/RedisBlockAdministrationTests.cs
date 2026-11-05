using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Networks;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Distributed;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class RedisBlockAdministrationTests(RedisSessionFixture fixture) : IClassFixture<RedisSessionFixture>
{
    [Fact]
    public async Task A_failed_audit_restores_the_original_redis_key_bytes()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        var blacklist = Blacklist(connection, "unblock-original");
        const string original = """
            { "Reason" : "Honey-token 'Original' tripped", "BlockedAtUtc" : "2026-10-01T15:30:00+03:00" }
            """;
        await fixture.Database.StringSetAsync("unblock-original:ipblock:198.51.100.113/32", original);
        var audit = new RejectingAudit(blacklist);
        var administration = new BlockAdministration(blacklist, audit);

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => administration.LiftAsync("198.51.100.113/32"));

        Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.113")));
        Assert.Equal(original, (string?)await fixture.Database.StringGetAsync("unblock-original:ipblock:198.51.100.113/32"));
        Assert.Equal(new BlockedRangeDto("198.51.100.113/32", "Honey-token 'Original' tripped",
            new DateTimeOffset(2026, 10, 1, 15, 30, 0, TimeSpan.FromHours(3))), Assert.Single(await blacklist.ListAsync()));
        Assert.False(audit.BlockedDuringWrite);
        Assert.Equal(1, audit.Attempts);
    }

    [Fact]
    public async Task Losing_redis_during_compensation_reports_an_unrecorded_lift()
    {
        await using var connection = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        var blacklist = Blacklist(connection, "unblock-unavailable");
        await blacklist.BlockAsync("198.51.100.113", "original reason");
        var audit = new RejectingAudit(blacklist) { BeforeFailure = () => connection.CloseAsync() };
        var administration = new BlockAdministration(blacklist, audit);

        var failure = await Assert.ThrowsAsync<UnblockUnrecordedException>(() => administration.LiftAsync("198.51.100.113/32"));

        Assert.Equal("The block was lifted, but recording and restoring it failed. Check the range before retrying.", failure.Message);
        Assert.False(await fixture.Database.KeyExistsAsync("unblock-unavailable:ipblock:198.51.100.113/32"));
        Assert.False(audit.BlockedDuringWrite);
        Assert.Equal(1, audit.Attempts);
    }

    private static RedisIpBlacklist Blacklist(IConnectionMultiplexer connection, string prefix)
        => new(connection, Options.Create(new RedisOptions { InstanceName = prefix }),
            Options.Create(new IntrusionResponseOptions()), NullLogger<RedisIpBlacklist>.Instance);

    private sealed class RejectingAudit(IIpBlacklist blacklist) : IAuditSink
    {
        public Func<Task>? BeforeFailure { get; init; }
        public bool BlockedDuringWrite { get; private set; }
        public int Attempts { get; private set; }

        public async Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            Attempts++;
            BlockedDuringWrite = await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.113"));
            if (BeforeFailure is not null)
            {
                await BeforeFailure();
            }

            throw new AuditWriteFailedException("Audit insert refused");
        }
    }
}
