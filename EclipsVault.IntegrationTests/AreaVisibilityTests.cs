using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class AreaVisibilityTests(VaultFixture vault)
{
    [Fact]
    public async Task An_administrator_is_offered_every_area()
    {
        var response = await (await vault.AdminClientAsync()).GetAsync("/api/ui/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([
            "overview", "secrets", "dynamic-secrets", "shared-with-me", "shared-by-me",
            "access", "access-requests", "security", "sign-ins", "sessions", "activity", "data", "profile",
            "users", "networks", "service-accounts", "audit", "notifications", "encryption", "license",
        ], me.GetProperty("areas").EnumerateArray().Select(area => area.GetString()!).ToArray());
    }

    [Fact]
    public async Task A_standard_account_is_offered_only_session_areas()
    {
        var session = await vault.SignInNewAccountAsync("it-standard-areas");
        var response = await session.Client.GetAsync("/api/ui/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([
            "overview", "secrets", "dynamic-secrets", "shared-with-me", "shared-by-me",
            "access", "access-requests", "security", "sign-ins", "sessions", "activity", "data", "profile",
        ], me.GetProperty("areas").EnumerateArray().Select(area => area.GetString()!).ToArray());
        // The vault is unlicensed, but only someone who can act on that is told.
        Assert.Equal(JsonValueKind.Null, me.GetProperty("licenseNotice").ValueKind);

    }

    [Theory]
    [InlineData("/Admin/Users", "198.18.110.201")]
    [InlineData("/Admin/CreateUser", "198.18.110.202")]
    [InlineData("/aDmIn/eDiTuSeR/11111111-1111-1111-1111-111111111111", "198.18.110.203")]
    [InlineData("/Admin/Networks", "198.18.110.204")]
    [InlineData("/ServiceAccounts", "198.18.110.205")]
    [InlineData("/Audit", "198.18.110.206")]
    [InlineData("/Notifications", "198.18.110.207")]
    [InlineData("/Encryption", "198.18.110.208")]
    [InlineData("/License", "198.18.110.209")]
    [InlineData("/Notifications/Index", "198.18.110.210")]
    [InlineData("/License/Index", "198.18.110.211")]
    public async Task A_standard_account_cannot_open_an_administration_page(string path, string source)
    {
        var session = await vault.SignInNewAccountAsync("it-area-denied-" + source.Split('.').Last(), IPAddress.Parse(source));
        var refused = await session.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Equal("/Account/Denied", refused.Headers.Location?.AbsolutePath);
    }
}
