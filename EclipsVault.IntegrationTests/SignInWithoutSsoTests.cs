using System.Net;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>Without single sign-on, nothing on Login leaves the vault, so its form-action stays 'self'.</summary>
[Collection(AuditTamperingCollection.Name)]
public sealed class SignInWithoutSsoTests(AuditTamperingFixture fixture)
{
    private const string AngularPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; frame-src 'none'; " +
        "frame-ancestors 'none'; form-action 'self'; base-uri 'self'" +
        "; require-trusted-types-for 'script'; trusted-types angular angular#bundler";

    [Fact]
    public async Task Login_without_single_sign_on_keeps_the_unchanged_Angular_policy()
    {
        using var response = await fixture.Vault.CreateClient().GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AngularPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }
}
