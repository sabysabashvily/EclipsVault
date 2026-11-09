using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class MonitoringPagesTests(VaultFixture vault)
{
    [Theory]
    [InlineData("/Notifications", "198.18.115.31")]
    [InlineData("/Notifications/Index", "198.18.115.32")]
    [InlineData("/License", "198.18.115.33")]
    [InlineData("/License/Index", "198.18.115.34")]
    [InlineData("/nOtIfIcAtIoNs/iNdEx", "198.18.115.35")]
    [InlineData("/lIcEnSe/", "198.18.115.36")]
    public async Task Each_bookmark_keeps_its_return_url_and_serves_Angular_only_to_administrators(string path, string source)
    {
        var anonymous = await vault.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal(path, System.Web.HttpUtility.ParseQueryString(anonymous.Headers.Location!.Query)["ReturnUrl"]);
        var reader = await vault.SignInNewAccountAsync("it-monitor-reader-" + source.Split('.').Last(), IPAddress.Parse(source));
        var denied = await reader.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/Denied", denied.Headers.Location?.AbsolutePath);
        var document = await (await vault.AdminClientAsync()).GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.True(document.Headers.CacheControl?.NoStore);
        Assert.Contains("<app-root>", await document.Content.ReadAsStringAsync());
        Assert.Contains("require-trusted-types-for 'script'", Assert.Single(document.Headers.GetValues("Content-Security-Policy")));
    }

    [Theory]
    [InlineData("Notifications", "/Notifications")]
    [InlineData("License", "/License")]
    [InlineData("View license", "/License")]
    public async Task Legacy_navigation_links_keep_their_destinations(string label, string destination)
    {
        var html = await (await vault.AdminClientAsync()).GetStringAsync("/Secrets");
        var link = Assert.Single(Regex.Matches(html, "<a\\b[^>]*>[\\s\\S]*?</a>")
            .Select(match => match.Value), anchor => Regex.Replace(anchor, "<[^>]*>", "").Trim() == label);

        Assert.Contains($"href=\"{destination}\"", link);
    }

    [Theory]
    [InlineData("POST", "/Notifications", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PUT", "/nOtIfIcAtIoNs/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("POST", "/Notifications/Index", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PATCH", "/NOTIFICATIONS/INDEX", HttpStatusCode.MethodNotAllowed)]
    [InlineData("DELETE", "/License", HttpStatusCode.MethodNotAllowed)]
    [InlineData("POST", "/lIcEnSe/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("POST", "/License/Index", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PUT", "/LICENSE/INDEX/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("GET", "/Notifications/Index/11111111-1111-1111-1111-111111111111", HttpStatusCode.NotFound)]
    [InlineData("GET", "/License/Index/11111111-1111-1111-1111-111111111111", HttpStatusCode.NotFound)]
    public async Task Retired_handlers_no_longer_answer(string method, string path, HttpStatusCode expected)
    {
        var admin = await vault.AdminClientAsync();
        var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            new HttpMethod(method), path);

        Assert.Equal(expected, response.StatusCode);
    }
}
