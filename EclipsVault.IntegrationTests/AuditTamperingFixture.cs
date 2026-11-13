using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A vault whose audit trail tests rewrite, truncate or hold. Verify reports the first broken entry of
/// the whole trail, so a tampered trail must never be seen by another test. Each test restores what it
/// tampered with, but the critical rows some verifications wrote stay, so tests here assert only on rows
/// after their own head.
/// </summary>
public sealed class AuditTamperingFixture : IAsyncLifetime
{
    public VaultFixture Vault { get; } = new();

    public Task InitializeAsync() => Vault.InitializeAsync();

    public Task DisposeAsync() => Vault.DisposeAsync();
}

// Program reads its configuration from process-wide environment variables, so this host starts alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AuditTamperingCollection : ICollectionFixture<AuditTamperingFixture>
{
    public const string Name = "audit tampering vault";
}
