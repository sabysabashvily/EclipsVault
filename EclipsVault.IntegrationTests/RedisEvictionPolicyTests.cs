using EclipsVault.Infrastructure.Distributed;
using Testcontainers.Redis;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A device's sign-out is a Redis key with an expiry. Managed services often default to
/// volatile-lru, which evicts exactly such keys under memory pressure, so the vault starts only on
/// a Redis that reports noeviction.
/// </summary>
public sealed class RedisEvictionPolicyTests
{
    [Fact]
    public async Task The_vault_connects_to_a_redis_that_never_evicts()
    {
        await using var redis = await StartRedisAsync("noeviction");

        await using var connection = RedisConnectionGuard.Connect(OptionsFor(redis));

        Assert.True(connection.IsConnected);
    }

    [Fact]
    public async Task The_vault_refuses_a_redis_that_may_evict_expiring_keys()
    {
        await using var redis = await StartRedisAsync("volatile-lru");

        var refused = Assert.Throws<InvalidOperationException>(() => RedisConnectionGuard.Connect(OptionsFor(redis)));

        Assert.Contains("maxmemory-policy volatile-lru", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Set maxmemory-policy to noeviction", refused.Message, StringComparison.Ordinal);
    }

    private static async Task<RedisContainer> StartRedisAsync(string evictionPolicy)
    {
        var redis = new RedisBuilder(RedisSessionFixture.RedisImage)
            .WithCommand("--maxmemory-policy", evictionPolicy)
            .Build();
        await redis.StartAsync();
        return redis;
    }

    // The container has no password; the credentials check has its own tests.
    private static RedisOptions OptionsFor(RedisContainer redis)
        => new() { Enabled = true, Configuration = redis.GetConnectionString(), AllowUnauthenticated = true };
}
