using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The UI API and the Public API each accept their own credential and nothing else (ADR-0005), and
/// the Angular UI's pages sit behind the session (ADR-0014). Each refused credential is first shown
/// to work where it belongs, so a test cannot pass because the credential was simply broken.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class AuthenticationBoundaryTests(VaultFixture vault)
{
    [Fact]
    public async Task Me_returns_the_signed_in_vault_account()
    {
        var admin = await vault.AdminClientAsync();

        var response = await admin.GetAsync("/api/ui/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(VaultFixture.AdminUsername, me.RootElement.GetProperty("username").GetString());
        Assert.Equal("Vault Administrator", me.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("Top Secret", me.RootElement.GetProperty("clearance").GetString());
        Assert.Equal("GLOBAL", me.RootElement.GetProperty("project").GetString());
    }

    [Fact]
    public async Task Me_refuses_an_api_key()
    {
        var apiKey = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-me-refuses-api-key");
        var service = vault.CreateClient();
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var publicApi = await service.GetAsync("/api/v1/secrets");
        var uiApi = await service.GetAsync("/api/ui/me");

        Assert.Equal(HttpStatusCode.OK, publicApi.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, uiApi.StatusCode);
    }

    [Fact]
    public async Task The_public_api_refuses_a_session()
    {
        var admin = await vault.AdminClientAsync();

        var uiApi = await admin.GetAsync("/api/ui/me");
        var publicApi = await admin.GetAsync("/api/v1/secrets");

        Assert.Equal(HttpStatusCode.OK, uiApi.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, publicApi.StatusCode);
    }

    /// <summary>
    /// Pins what a bookmark sees before the Angular document is served.
    /// </summary>
    [Theory]
    [InlineData("/Access", "?ReturnUrl=%2FAccess")]
    [InlineData("/Activity", "?ReturnUrl=%2FActivity")]
    [InlineData("/Activity/Index", "?ReturnUrl=%2FActivity%2FIndex")]
    [InlineData("/SignIns", "?ReturnUrl=%2FSignIns")]
    [InlineData("/SignIns/Index", "?ReturnUrl=%2FSignIns%2FIndex")]
    [InlineData("/Security", "?ReturnUrl=%2FSecurity")]
    [InlineData("/Security/Index", "?ReturnUrl=%2FSecurity%2FIndex")]
    public async Task An_anonymous_request_for_an_angular_owned_path_is_sent_to_sign_in(string path, string query)
    {
        var anonymous = vault.CreateClient();

        var response = await anonymous.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        var target = location.IsAbsoluteUri ? location : new Uri(anonymous.BaseAddress!, location);
        Assert.Equal("/Account/Login", target.AbsolutePath);
        Assert.Equal(query, target.Query);
    }
}
