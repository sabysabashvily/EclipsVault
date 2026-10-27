using System.Net;
using EclipsVault.Infrastructure.Distributed;
using Xunit;

namespace EclipsVault.Tests.Distributed;

/// <summary>
/// Redis holds the answers to "is this session revoked?" and "is this address blocked?", so a Redis
/// anyone can write is a vault whose kill switches are advisory: delete a revocation marker and a
/// session its owner signed out of works again; clear a blacklist entry and the honey-token trap
/// releases whoever it just caught.
///
/// Redis listens on every interface and needs no password by default, so the insecure configuration
/// is the one you get by not thinking about it — which is exactly why this refuses at startup
/// rather than trusting anyone to notice.
/// </summary>
public class RedisConnectionGuardTests
{
    private static RedisOptions Options(string configuration, bool allowUnauthenticated = false)
        => new() { Enabled = true, Configuration = configuration, AllowUnauthenticated = allowUnauthenticated };

    [Fact]
    public void The_default_local_redis_is_refused()
    {
        // The value that shipped in appsettings.json, and the one a developer reaches for.
        var ex = Assert.Throws<InvalidOperationException>(
            () => RedisConnectionGuard.RequireAuthentication(Options("localhost:6379")));

        Assert.Contains("no password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_password_satisfies_it()
        => RedisConnectionGuard.RequireAuthentication(Options("localhost:6379,password=dev-redis-password"));

    [Fact]
    public void An_acl_user_satisfies_it()
        => RedisConnectionGuard.RequireAuthentication(Options("cache:6379,user=eclipsvault,password=s3cret"));

    [Fact]
    public void Tls_without_a_password_is_still_refused()
    {
        // Encrypting the wire does not decide who may write the revocation markers.
        Assert.Throws<InvalidOperationException>(
            () => RedisConnectionGuard.RequireAuthentication(Options("cache:6379,ssl=true")));
    }

    [Fact]
    public void An_empty_password_is_not_a_password()
    {
        Assert.Throws<InvalidOperationException>(
            () => RedisConnectionGuard.RequireAuthentication(Options("localhost:6379,password=")));
    }

    [Fact]
    public void The_escape_hatch_works_for_someone_who_means_it()
        => RedisConnectionGuard.RequireAuthentication(Options("localhost:6379", allowUnauthenticated: true));

    [Fact]
    public void An_unparseable_configuration_says_so_rather_than_passing()
    {
        // Failing open on a string we could not understand would be the worst of both.
        var ex = Assert.Throws<InvalidOperationException>(
            () => RedisConnectionGuard.RequireAuthentication(Options("localhost:6379,ssl=not-a-bool")));

        Assert.Contains("could not be parsed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_policy_that_could_not_be_read_is_refused_with_the_fix()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => RedisConnectionGuard.RequireNoEviction(new DnsEndPoint("cache", 6379), policy: null));

        Assert.Contains("could not read maxmemory_policy from INFO memory", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Set maxmemory-policy to noeviction", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_allkeys_policy_is_refused_too()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => RedisConnectionGuard.RequireNoEviction(new DnsEndPoint("cache", 6379), "allkeys-lru"));

        Assert.Contains("maxmemory-policy allkeys-lru", ex.Message, StringComparison.Ordinal);
    }
}
