using System.Net;
using Xunit;

namespace EclipsVault.IntegrationTests;

internal static class UiApiRequest
{
    public static async Task<HttpResponseMessage> WriteWithAntiforgeryAsync(VaultSession session, HttpMethod method, string path, HttpContent? content = null)
    {
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/ui/me")).StatusCode);
        var token = Assert.IsType<Cookie>(session.Cookies.GetCookies(session.Client.BaseAddress!)["XSRF-TOKEN"]);
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("RequestVerificationToken", token.Value);
        return await session.Client.SendAsync(request);
    }
}
