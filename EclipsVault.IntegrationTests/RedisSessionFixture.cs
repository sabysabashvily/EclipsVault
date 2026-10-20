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
    private readonly RedisContainer _container = new RedisBuilder(
        "redis:8.2-alpine@sha256:b51665e66f00759be7c3152ad5ac3c66fb2f619c13ef62dea7cc1f9914524635").Build();

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
        => new(connection, Options.Create(new RedisOptions { InstanceName = "session-tests" }),
            NullLogger<RedisSessionRegistry>.Instance);

    public async Task InterruptRevocationAfterTombstoneAsync(Guid userId, Guid sessionId)
    {
        // A node may stop between writing the marker and removing metadata.
        await _connection!.GetDatabase().StringSetAsync(
            $"session-tests:session-revoked:{userId:N}:{sessionId:N}", "1", TimeSpan.FromHours(24));
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
