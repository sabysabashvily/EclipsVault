using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A vault whose production window never opens. The ordinary vault keeps the real window, which is
/// open or closed depending on when the suite runs, so only a vault of its own can pin the closed case.
/// </summary>
public sealed class ClosedProductionWindowFixture : IAsyncLifetime
{
    public VaultFixture Vault { get; } = new() { ProductionWindowNeverOpens = true };

    public Task InitializeAsync() => Vault.InitializeAsync();

    public Task DisposeAsync() => Vault.DisposeAsync();
}

// Program reads its configuration from process-wide environment variables, so this host starts alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ClosedProductionWindowCollection : ICollectionFixture<ClosedProductionWindowFixture>
{
    public const string Name = "closed production window vault";
}
