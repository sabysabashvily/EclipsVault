using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>Sign-in steps as the Angular Login page takes them, with the request token its document set.</summary>
internal static class SignInProof
{
    /// <summary>The password stage, which must pass; the client then holds a pending sign-in.</summary>
    public static async Task BeginPasswordAsync(HttpClient client, string username, string password)
    {
        var response = await UiApiRequest.SendWithTokenAsync(client, await SignInDocument.TokenAsync(client), HttpMethod.Post,
            "/api/ui/auth/password", JsonContent.Create(new { username, password }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>A passkey sign-in call from the Login page, which must answer 200.</summary>
    public static async Task<JsonElement> PostPasskeyAsync(HttpClient client, string path, object body)
    {
        var response = await SendPasskeyAsync(client, path, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A passkey sign-in call from the Login page, whatever it answers.</summary>
    public static async Task<HttpResponseMessage> SendPasskeyAsync(HttpClient client, string path, object body)
        => await UiApiRequest.SendWithTokenAsync(client, await SignInDocument.TokenAsync(client), HttpMethod.Post,
            path, JsonContent.Create(body));
}
