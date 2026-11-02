using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A vault whose bootstrap administrator is its only enabled administrator. Over HTTP the signed-in
/// administrator is always enabled, so the last-administrator rule is reachable only by demoting
/// yourself in a vault with no other; the ordinary vault's tests create many administrators.
/// </summary>
public sealed class SingleAdministratorFixture : IAsyncLifetime
{
    public VaultFixture Vault { get; } = new();

    public Task InitializeAsync() => Vault.InitializeAsync();

    public Task DisposeAsync() => Vault.DisposeAsync();
}

// Program reads its configuration from process-wide environment variables, so this host starts alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SingleAdministratorCollection : ICollectionFixture<SingleAdministratorFixture>
{
    public const string Name = "single administrator vault";
}
