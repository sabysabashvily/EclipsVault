using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>A vault with a SQL Server database of its own to mint dynamic credentials on.</summary>
public sealed class DynamicSecretsFixture : IAsyncLifetime
{
    public VaultFixture Vault { get; } = new() { DynamicSecretsTarget = true };

    public Task InitializeAsync() => Vault.InitializeAsync();

    public Task DisposeAsync() => Vault.DisposeAsync();
}

// The vault's startup reads process-wide environment variables, so this collection runs alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DynamicSecretsCollection : ICollectionFixture<DynamicSecretsFixture>
{
    public const string Name = "dynamic-secrets-vault";
}
