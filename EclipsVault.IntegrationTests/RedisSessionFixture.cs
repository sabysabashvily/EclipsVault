using EclipsVault.Infrastructure.Distributed;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class RedisSessionFixture : IAsyncLifetime
{
    /// <summary>Redis 7.4, the version <c>docker-compose.yml</c> runs, pinned by digest.</summary>
    private const string RedisImage =
        "redis:7.4-alpine@sha256:858f009f9709ce576febc734aa78b8f6d624b82571f9ddb6bda4377c833b3499";

    private const string InstanceName = "session-tests";

    private readonly RedisContainer _container = new RedisBuilder(RedisImage).Build();

    private ConnectionMultiplexer? _connection;
    private ConnectionMultiplexer? _peerConnection;

    public RedisSessionRegistry Registry { get; private set; } = null!;
    public RedisSessionRegistry PeerRegistry { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        _peerConnection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        Registry = CreateRegistry(_connection);
        PeerRegistry = CreateRegistry(_peerConnection);
    }

    private static RedisSessionRegistry CreateRegistry(IConnectionMultiplexer connection)
        => new(connection, Options.Create(new RedisOptions { InstanceName = InstanceName }),
            NullLogger<RedisSessionRegistry>.Instance);

    public async Task InterruptRevocationAfterTombstoneAsync(Guid userId, Guid sessionId)
    {
        // A node may stop between writing the marker and removing metadata.
        await _connection!.GetDatabase().StringSetAsync(
            $"{InstanceName}:session-revoked:{userId:N}:{sessionId:N}", "1", TimeSpan.FromHours(24));
    }

    public async Task DisposeAsync()
    {
        if (_peerConnection is not null)
        {
            await _peerConnection.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
