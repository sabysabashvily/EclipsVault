using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Tests.Fakes;
using Xunit;

namespace EclipsVault.Tests.Security;

/// <summary>The Vault Transit engine's reading of its key, against an in-process Transit server.</summary>
public sealed class VaultTransitCryptoEngineTests
{
    [Fact]
    public async Task The_master_keys_are_the_key_versions_Vault_still_decrypts_latest_first()
    {
        var transit = new FakeVaultTransit();
        transit.RotateKey();
        transit.RotateKey();
        transit.MinDecryptionVersion = 2;

        var keys = await transit.Engine().GetMasterKeysAsync(default);

        Assert.Equal("vault:eclipsvault:v3", keys.CurrentKekId);
        Assert.Equal(["vault:eclipsvault:v3", "vault:eclipsvault:v2"], keys.KnownKekIds);
        Assert.True(keys.KeyService);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"data":{"latest_version":"two","min_decryption_version":1}}""")]
    [InlineData("""{"data":{"latest_version":0,"min_decryption_version":1}}""")]
    [InlineData("""{"data":{"latest_version":2,"min_decryption_version":3}}""")]
    public async Task An_unreadable_key_description_is_a_configuration_error(string description)
    {
        var transit = new FakeVaultTransit { KeyDescription = description };

        await Assert.ThrowsAsync<CryptoConfigurationException>(() => transit.Engine().GetMasterKeysAsync(default));
    }
}
