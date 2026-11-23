using EclipsVault.Infrastructure.Security;
using Xunit;

namespace EclipsVault.Tests.Security;

public class VaultTransitFormatTests
{
    [Theory]
    [InlineData("vault:v1:Zm9vYmFy", "vault:eclipsvault:v1")]
    [InlineData("vault:v42:abcdef", "vault:eclipsvault:v42")]
    public void KekId_surfaces_the_wrapping_key_version(string ciphertext, string expected)
        => Assert.Equal(expected, VaultTransitFormat.KekId("eclipsvault", ciphertext));

    [Fact]
    public void KekId_honours_the_configured_key_name()
        => Assert.Equal("vault:prod-master:v3", VaultTransitFormat.KekId("prod-master", "vault:v3:abc"));

    [Fact]
    public void KekId_is_defensive_against_a_malformed_ciphertext()
        => Assert.Equal("vault:eclipsvault:v?", VaultTransitFormat.KekId("eclipsvault", "garbage"));

    [Theory]
    [InlineData("""{"errors":["invalid ciphertext: no prefix"]}""")]
    [InlineData("""{"errors":["invalid ciphertext: version is too new"]}""")]
    [InlineData("""{"errors":["invalid ciphertext: could not decode base64"]}""")]
    [InlineData("""{"errors":["ciphertext or signature version is disallowed by policy (too old)"]}""")]
    [InlineData("""{"errors":["cipher: message authentication failed"]}""")]
    public void A_400_that_names_only_the_ciphertext_blames_the_ciphertext(string errorBody)
        => Assert.True(VaultTransitFormat.BlamesTheCiphertext(errorBody));

    [Theory]
    [InlineData("""{"errors":["encryption key not found"]}""")]
    [InlineData("""{"errors":["missing 'context' for key derivation; the key was created using a derived key"]}""")]
    [InlineData("""{"errors":["missing ciphertext to decrypt"]}""")]
    [InlineData("""{"errors":[]}""")]
    [InlineData("""{"errors":["cipher: message authentication failed","encryption key not found"]}""")]
    [InlineData("not json")]
    public void A_400_about_the_key_or_the_request_does_not_blame_the_ciphertext(string errorBody)
        => Assert.False(VaultTransitFormat.BlamesTheCiphertext(errorBody));
}
