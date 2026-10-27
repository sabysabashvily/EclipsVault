using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// Holds each account's session revocation version in process memory. Suitable for a single node;
/// multi-node deployments use the Redis-backed implementation so a revocation reaches every node.
/// A restart loses every version, which refuses every existing session: the versions they carry
/// may have been revoked in the state the restart lost.
/// </summary>
public sealed class InMemorySessionRevocationService : ISessionRevocationService
{
    private readonly ConcurrentDictionary<Guid, string> _versions = new();
    private readonly ILogger<InMemorySessionRevocationService> _logger;

    public InMemorySessionRevocationService(ILogger<InMemorySessionRevocationService> logger) => _logger = logger;

    // GetOrAdd may build two candidates in a race, but stores and returns only one of them.
    public Task<string> CaptureAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(_versions.GetOrAdd(userId, _ => NewVersion()));

    public Task<string?> FindAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(_versions.TryGetValue(userId, out var version) ? version : null);

    public Task RevokeAsync(Guid userId, CancellationToken ct = default)
    {
        _versions[userId] = NewVersion();
        _logger.LogWarning("Every session of user {UserId} was revoked", userId);
        return Task.CompletedTask;
    }

    private static string NewVersion() => Guid.NewGuid().ToString("N");
}
