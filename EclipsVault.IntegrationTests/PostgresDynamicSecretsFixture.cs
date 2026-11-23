using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>A vault with a PostgreSQL database of its own to mint dynamic credentials on, and no SQL Server target.</summary>
public sealed class PostgresDynamicSecretsFixture : IAsyncLifetime
{
    public VaultFixture Vault { get; } = new() { PostgresDynamicSecretsTarget = true };

    public Task InitializeAsync() => Vault.InitializeAsync();

    public Task DisposeAsync() => Vault.DisposeAsync();
}

// The vault's startup reads process-wide environment variables, so this collection runs alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresDynamicSecretsCollection : ICollectionFixture<PostgresDynamicSecretsFixture>
{
    public const string Name = "postgres-dynamic-secrets-vault";
}
