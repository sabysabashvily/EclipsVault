using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EclipsVault.IntegrationTests;

internal static class SignInProof
{
    public static async Task BeginPasswordAsync(HttpClient client, string username, string password)
    {
        var page = await client.GetStringAsync("/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(page, "/Account/Login"),
            ["Username"] = username,
            ["Password"] = password,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public static async Task<JsonElement> PostPasskeyAsync(HttpClient client, string path, object body)
    {
        var page = await client.GetStringAsync("/Account/Login");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", VaultFixture.FormTokenOf(page, "/Account/Login"));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
