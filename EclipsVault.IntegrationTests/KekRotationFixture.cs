using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A vault that also loads a retired master key, so its tests can seal secrets under that key and
/// rotate them. Rotation re-wraps every pending row in the vault, so it needs a vault of its own.
/// </summary>
public sealed class KekRotationFixture : IAsyncLifetime
{
    public KekRotationFixture() => Vault = new() { RetiredMasterKey = RetiredMasterKey };

    public string RetiredMasterKey { get; } = SecretsUnderMasterKey.NewMasterKey();

    public VaultFixture Vault { get; }

    public Task InitializeAsync() => Vault.InitializeAsync();

    public Task DisposeAsync() => Vault.DisposeAsync();
}

// Program reads its configuration from process-wide environment variables, so this host starts alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KekRotationCollection : ICollectionFixture<KekRotationFixture>
{
    public const string Name = "master key rotation vault";
}
