using Xunit;

namespace EclipsVault.IntegrationTests;

public sealed class ProofDisclosureTests
{
    [Theory]
    [InlineData("{\"@sp\":\"0b01123456362157\"}", "123456", false)]
    [InlineData("{\"code\":\"123456\"}", "123456", true)]
    [InlineData("The submitted code was 123456.", "123456", true)]
    [InlineData("before-Mailbox-Code-Disclosure-Sentinel-after", "Mailbox-Code-Disclosure-Sentinel", true)]
    public void Disclosure_checks_match_credential_values_without_mistaking_trace_digits_for_a_code(string text, string secret, bool disclosed)
        => Assert.Equal(disclosed, ProofDisclosure.ContainsSecret(text, secret));
}
