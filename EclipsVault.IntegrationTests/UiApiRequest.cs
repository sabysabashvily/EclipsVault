using System.Net;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace EclipsVault.IntegrationTests;

internal static class UiApiRequest
{
    public static async Task<HttpResponseMessage> WriteWithAntiforgeryAsync(VaultSession session, HttpMethod method, string path, HttpContent? content = null)
    {
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/ui/me")).StatusCode);
        var token = Assert.IsType<Cookie>(session.Cookies.GetCookies(session.Client.BaseAddress!)["XSRF-TOKEN"]);
        return await SendWithTokenAsync(session.Client, token.Value, method, path, content);
    }

    /// <summary>
    /// The session's antiforgery token, as the Angular UI gets it: the cookie <c>/api/ui/me</c> sets on every
    /// answer. Read from the answer itself, so it also serves a client whose cookie jar the test cannot see.
    /// </summary>
    public static async Task<string> AntiforgeryTokenAsync(HttpClient client)
    {
        using var me = await client.GetAsync("/api/ui/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var cookies = SetCookieHeaderValue.ParseList(me.Headers.GetValues(HeaderNames.SetCookie).ToList());
        return Assert.Single(cookies, cookie => cookie.Name == "XSRF-TOKEN").Value.ToString();
    }

    public static async Task<HttpResponseMessage> SendWithTokenAsync(HttpClient client, string token, HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request);
    }
}
