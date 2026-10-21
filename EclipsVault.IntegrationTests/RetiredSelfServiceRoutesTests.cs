using System.Net;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class RetiredSelfServiceRoutesTests(VaultFixture vault)
{
    [Fact]
    public async Task The_Legacy_sidebar_keeps_links_to_the_Angular_owned_urls()
    {
        var client = await vault.AdminClientAsync();

        var page = await client.GetStringAsync("/Secrets");

        Assert.Contains("href=\"/Sessions\"", page, StringComparison.Ordinal);
        Assert.Contains("href=\"/Data\"", page, StringComparison.Ordinal);
        Assert.Contains("href=\"/Secrets/SharedByMe\"", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Sessions/Revoke")]
    [InlineData("/Sessions/RevokeOthers")]
    [InlineData("/Data/Download")]
    [InlineData("/Secrets/RevokeSharedByMe")]
    [InlineData("/sEsSiOnS/rEvOkE/11111111-1111-1111-1111-111111111111")]
    [InlineData("/sEsSiOnS/rEvOkEoThErS/")]
    [InlineData("/dAtA/dOwNlOaD/legacy")]
    [InlineData("/sEcReTs/rEvOkEsHaReDbYmE/11111111-1111-1111-1111-111111111111")]
    public async Task A_retired_write_has_no_handler_even_with_a_session_and_valid_antiforgery(string path)
    {
        var client = await vault.AdminClientAsync();
        using var me = await client.GetAsync("/api/ui/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var cookies = SetCookieHeaderValue.ParseList(me.Headers.GetValues(HeaderNames.SetCookie).ToList());
        var token = Assert.Single(cookies, cookie => cookie.Name == "XSRF-TOKEN").Value.ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = "11111111-1111-1111-1111-111111111111",
                ["grantId"] = "11111111-1111-1111-1111-111111111111"
            })
        };
        request.Headers.Add("RequestVerificationToken", token);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/Sessions/Index/legacy")]
    [InlineData("/Data/Index/legacy")]
    [InlineData("/Secrets/SharedByMe/legacy")]
    public async Task A_retired_view_cannot_be_reached_through_the_conventional_route(string path)
    {
        var client = await vault.AdminClientAsync();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
