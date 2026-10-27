using System.Net;
using StackExchange.Redis;

namespace EclipsVault.Infrastructure.Distributed;

/// <summary>
/// Refuses to put the vault's security state in a Redis that anyone can write, or that may evict it.
///
/// When Redis is enabled it holds the session-revocation markers, the intrusion IP blacklist, the
/// auth throttle and the encrypted-envelope cache. The first two are answers the vault trusts: an
/// unauthenticated Redis lets anyone who can reach the port delete a revocation marker — restoring
/// a session its owner had signed out of — or clear a block the intrusion response just placed.
/// "Sign out everywhere" then means nothing, and nor does the honey-token trap.
///
/// Redis listens on every interface and needs no password out of the box, so the insecure
/// configuration is the one you get by not thinking about it. This makes not thinking about it fail
/// at startup rather than in an incident.
/// </summary>
public static class RedisConnectionGuard
{
    private const string NoEviction = "noeviction";

    /// <summary>
    /// Connects to the configured Redis, refusing one without credentials or one that may evict keys.
    /// Connecting at startup also fails fast when the store holding security state is unreachable.
    /// </summary>
    public static ConnectionMultiplexer Connect(RedisOptions options)
    {
        RequireAuthentication(options);
        RequireNoEvictionOnEveryServer(options);
        return ConnectionMultiplexer.Connect(options.Configuration);
    }

    /// <summary>
    /// Refuses a Redis whose eviction policy is not noeviction, or whose policy could not be read.
    /// A device's sign-out is a key with an expiry, which any volatile-* or allkeys-* policy may
    /// evict under memory pressure; the vault would then accept a copy of that device's cookie again.
    /// </summary>
    public static void RequireNoEviction(EndPoint server, string? policy)
    {
        if (policy == NoEviction)
        {
            return;
        }

        var found = policy is null
            ? $"The vault could not read maxmemory_policy from INFO memory on Redis {server}"
            : $"Redis {server} runs with maxmemory-policy {policy}";
        throw new InvalidOperationException(
            found + ". The vault keeps each device's sign-out in Redis as a key with an expiry, and any " +
            "policy other than noeviction may evict it under memory pressure, letting a copy of that " +
            "device's cookie back in. Set maxmemory-policy to noeviction (in redis.conf, or the managed " +
            "service's parameter of that name), make sure INFO is allowed, then start the vault again.");
    }

    private static void RequireNoEvictionOnEveryServer(RedisOptions options)
    {
        // The client treats INFO as an admin command, so the policy is read on a short-lived
        // connection; the vault's own connection never gets admin commands.
        var probe = ConfigurationOptions.Parse(options.Configuration);
        probe.AllowAdmin = true;
        using var connection = ConnectionMultiplexer.Connect(probe);
        foreach (var server in connection.GetServers())
        {
            RequireNoEviction(server.EndPoint, ReadEvictionPolicy(server));
        }
    }

    private static string? ReadEvictionPolicy(IServer server)
    {
        try
        {
            return server.Info("memory")
                .SelectMany(section => section)
                .Where(entry => entry.Key == "maxmemory_policy")
                .Select(entry => entry.Value)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            return null;
        }
    }

    public static void RequireAuthentication(RedisOptions options)
    {
        if (options.AllowUnauthenticated)
        {
            return;
        }

        ConfigurationOptions parsed;
        try
        {
            parsed = ConfigurationOptions.Parse(options.Configuration);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Redis:Configuration could not be parsed ('{options.Configuration}'): {ex.Message}", ex);
        }

        // A password may also arrive as a Redis 6 ACL user; either satisfies this.
        if (!string.IsNullOrEmpty(parsed.Password) || !string.IsNullOrEmpty(parsed.User))
        {
            return;
        }

        throw new InvalidOperationException(
            "Redis is enabled with no password, and it is about to hold this vault's session revocations " +
            "and intrusion blocks — anyone who can reach it could restore a signed-out session or lift a " +
            "block. Add credentials to Redis:Configuration (for example " +
            "'localhost:6379,password=…', or ',user=…,password=…' for an ACL user). Set " +
            "Redis:AllowUnauthenticated=true only if the instance is genuinely unreachable by anything " +
            "else.");
    }
}
