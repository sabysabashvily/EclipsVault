using System.Net;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class DocumentSecurityHeadersTests(VaultFixture vault)
{
    // Deliberately independent of the implementation: pin every directive in both complete variants.
    private const string LegacyPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; frame-src 'none'; " +
        "frame-ancestors 'none'; form-action 'self'; base-uri 'self'";

    private const string AngularPolicy = LegacyPolicy +
        "; require-trusted-types-for 'script'; trusted-types angular angular#bundler";

    [Theory]
    [InlineData("/Profile")]
    [InlineData("/pRoFiLe/InDeX")]
    [InlineData("/profile/changepassword")]
    [InlineData("/PROFILE/RECOVERYCODES")]
    [InlineData("/Access")]
    [InlineData("/Access/Index")]
    [InlineData("/access?from=bookmark")]
    [InlineData("/ACCESS/INDEX/")]
    [InlineData("/Activity")]
    [InlineData("/Activity/Index")]
    [InlineData("/activity?page=3")]
    [InlineData("/SignIns")]
    [InlineData("/SignIns/Index")]
    [InlineData("/signins")]
    [InlineData("/Security")]
    [InlineData("/Security/Index")]
    [InlineData("/security")]
    [InlineData("/SECURITY/index")]
    [InlineData("/Sessions")]
    [InlineData("/Sessions/Index")]
    [InlineData("/sessions")]
    [InlineData("/SESSIONS/index")]
    [InlineData("/Data")]
    [InlineData("/Data/Index")]
    [InlineData("/data")]
    [InlineData("/DATA/index")]
    [InlineData("/Secrets/SharedByMe")]
    [InlineData("/secrets/sharedbyme")]
    [InlineData("/Secrets/SharedWithMe")]
    [InlineData("/secrets/sharedwithme")]
    public async Task Angular_documents_add_only_the_two_Trusted_Types_directives(string path)
    {
        var client = await vault.AdminClientAsync();
        using var get = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("text/html", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(AngularPolicy, Assert.Single(get.Headers.GetValues("Content-Security-Policy")));
        Assert.True(get.Headers.CacheControl?.NoStore);
        Assert.Contains("<app-root>", await get.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var headRequest = new HttpRequestMessage(HttpMethod.Head, path);
        using var head = await client.SendAsync(headRequest);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal("text/html", head.Content.Headers.ContentType?.MediaType);
        Assert.Equal(AngularPolicy, Assert.Single(head.Headers.GetValues("Content-Security-Policy")));
        Assert.True(head.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("/Account/Login", HttpStatusCode.Redirect)]
    [InlineData("/Account/Denied", HttpStatusCode.OK)]
    [InlineData("/Secrets", HttpStatusCode.OK)]
    [InlineData("/Access/NotAnAngularRoute", HttpStatusCode.NotFound)]
    [InlineData("/api/ui/me", HttpStatusCode.OK)]
    [InlineData("/css/site.css", HttpStatusCode.OK)]
    public async Task Responses_outside_the_Angular_document_handler_keep_the_complete_legacy_policy(
        string path, HttpStatusCode status)
    {
        var client = await vault.AdminClientAsync();
        using var response = await client.GetAsync(path);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(LegacyPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Theory]
    [InlineData("/Profile")]
    [InlineData("/pRoFiLe/InDeX")]
    [InlineData("/profile/changepassword")]
    [InlineData("/PROFILE/RECOVERYCODES")]
    [InlineData("/Access")]
    [InlineData("/Access/Index")]
    public async Task Anonymous_Angular_requests_keep_the_legacy_policy_on_the_sign_in_redirect(string path)
    {
        using var client = vault.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/Account/Login?ReturnUrl=", response.Headers.Location?.AbsoluteUri);
        Assert.Equal(LegacyPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));

        using var login = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("text/html", login.Content.Headers.ContentType?.MediaType);
        Assert.Equal(LegacyPolicy, Assert.Single(login.Headers.GetValues("Content-Security-Policy")));
    }
}
