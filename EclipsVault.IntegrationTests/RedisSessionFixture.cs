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
    internal const string RedisImage =
        "redis:7.4-alpine@sha256:858f009f9709ce576febc734aa78b8f6d624b82571f9ddb6bda4377c833b3499";

    private static readonly RedisOptions OptionsValue = new() { InstanceName = "session-tests" };

    // The vault refuses to start on any other policy; set explicitly so the image default cannot change it.
    private readonly RedisContainer _container = new RedisBuilder(RedisImage)
        .WithCommand("--maxmemory-policy", "noeviction")
        .Build();

    private ConnectionMultiplexer? _connection;
    private ConnectionMultiplexer? _peerConnection;

    public RedisSessionRegistry Registry { get; private set; } = null!;
    public RedisSessionRegistry PeerRegistry { get; private set; } = null!;

    public RedisSessionRevocationService Revocation { get; private set; } = null!;
    public RedisSessionRevocationService PeerRevocation { get; private set; } = null!;
    public IDatabase Database => _connection!.GetDatabase();
    public string ConnectionString => _container.GetConnectionString();
    public string SessionPrefix => OptionsValue.SessionKeyPrefix;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        _peerConnection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        Registry = CreateRegistry(_connection);
        PeerRegistry = CreateRegistry(_peerConnection);
        Revocation = CreateRevocation(_connection);
        PeerRevocation = CreateRevocation(_peerConnection);
    }

    private static RedisSessionRegistry CreateRegistry(IConnectionMultiplexer connection)
        => new(connection, Options.Create(OptionsValue),
            NullLogger<RedisSessionRegistry>.Instance);

    private static RedisSessionRevocationService CreateRevocation(IConnectionMultiplexer connection)
        => new(connection, Options.Create(OptionsValue), NullLogger<RedisSessionRevocationService>.Instance);

    public Task PauseAsync() => _container.PauseAsync();
    public async Task ResumeAsync()
    {
        // Restarting a Docker container remaps its random host port; pausing preserves the endpoint
        // already configured on the running vault nodes while making the real store unavailable.
        await _container.UnpauseAsync();
        await using var ready = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        await ready.GetDatabase().PingAsync();
    }

    public async Task InterruptRevocationAfterTombstoneAsync(Guid userId, Guid sessionId)
    {
        // A node may stop between writing the marker and removing metadata.
        await _connection!.GetDatabase().StringSetAsync(
            $"{SessionPrefix}:session-revoked:{userId:N}:{sessionId:N}", "1", TimeSpan.FromHours(24));
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
