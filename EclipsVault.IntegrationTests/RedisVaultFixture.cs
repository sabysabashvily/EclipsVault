using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class RedisVaultFixture : IAsyncLifetime
{
    public RedisSessionFixture Redis { get; } = new();
    public VaultFixture Vault { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Redis.InitializeAsync();
        Vault = new VaultFixture { RedisConfiguration = Redis.ConnectionString };
        await Vault.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await Vault.DisposeAsync();
        await Redis.DisposeAsync();
    }
}

// These hosts configure the real Program through process-wide deployment settings.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RedisVaultCollection : ICollectionFixture<RedisVaultFixture>
{
    public const string Name = "redis-production-vault";
}
