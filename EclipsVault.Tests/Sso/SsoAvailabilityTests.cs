using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.Tests.Sso;

/// <summary>
/// The identity provider's origin joins the sign-in page's form-action, so it must be a bare
/// origin from configuration: never a path, a query, or anything that is not http or https.
/// </summary>
public class SsoAvailabilityTests
{
    [Theory]
    [InlineData(true, "https://idp.example.com/realms/vault", "https://idp.example.com")]
    [InlineData(true, "https://IdP.Example.com:8443/realms/vault?x=1; script-src *", "https://idp.example.com:8443")]
    [InlineData(true, "http://localhost:8080/realms/dev", "http://localhost:8080")]
    [InlineData(true, "https://bücher.example/realms/vault", "https://xn--bcher-kva.example")]
    [InlineData(true, "https://[2001:db8::1]:8443/realms/vault", "https://[2001:db8::1]:8443")]
    [InlineData(true, "javascript:alert(1)", "")]
    [InlineData(true, "idp.example.com", "")]
    [InlineData(true, "", "")]
    [InlineData(false, "https://idp.example.com/realms/vault", "")]
    public void The_origin_is_the_authority_s_scheme_host_and_port_only_while_enabled(bool enabled, string authority, string origin)
    {
        var sso = new SsoAvailability(Options.Create(new SsoOptions { Enabled = enabled, Authority = authority }));

        Assert.Equal(origin, sso.IdentityProviderOrigin);
    }
}
