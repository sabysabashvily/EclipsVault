using System.Net;
using EclipsVault.Infrastructure.Distributed;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace EclipsVault.Tests.Distributed;

/// <summary>
/// A break-glass lift that fails partway on Redis puts back what it removed, keeps the source
/// refused, and tells the operator which removal it cannot vouch for (ADR-0032).
/// </summary>
public sealed class RedisRecoveryLiftFailureTests
{
    private static readonly IPAddress Source = IPAddress.Parse("198.51.100.120");

    [Fact]
    public async Task A_removal_that_timed_out_is_reported_as_unconfirmed_and_the_source_stays_refused()
    {
        var store = new RedisBlacklistStore { RemovalWithoutReply = "lift-failure:ipblock:198.51.100.0/24" };
        var log = new CriticalLog();
        var blacklist = new RedisIpBlacklist(store.Connection, Options.Create(new RedisOptions { InstanceName = "lift-failure" }),
            Options.Create(new IntrusionResponseOptions()), log);
        var database = store.Connection.GetDatabase();
        await database.StringSetAsync("lift-failure:ipblock:198.51.100.120/32", "host", when: When.NotExists);
        await database.StringSetAsync("lift-failure:ipblock:198.51.100.0/24", "range", when: When.NotExists);

        await Assert.ThrowsAsync<RedisTimeoutException>(() => blacklist.LiftForRecoveryAsync(Source));

        Assert.Equal("host", (string?)await database.StringGetAsync("lift-failure:ipblock:198.51.100.120/32"));
        Assert.True(await blacklist.IsBlockedAsync(Source));
        var critical = Assert.Single(log.Messages);
        Assert.Contains("possibly removing 198.51.100.0/24 with its record unknown (restored: False)", critical, StringComparison.Ordinal);
        Assert.Contains("lift-failure:ipguard:198.51.100.120/32", critical, StringComparison.Ordinal);
    }

    private sealed class CriticalLog : ILogger<RedisIpBlacklist>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Critical)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
