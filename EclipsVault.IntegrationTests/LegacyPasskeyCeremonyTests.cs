using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class LegacyPasskeyCeremonyTests(VaultFixture vault)
{
    [Fact]
    public async Task A_counterless_passkey_assertion_presented_twice_signs_in_only_once()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-legacy-key-once", "100.72.87.1");
        using var key = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner.Browser, key, "Counterless");
        var browser = vault.CreateSession(source: IPAddress.Parse("100.72.87.2"));
        var options = await SignInProof.PostPasskeyAsync(browser.Client, "/Account/PasskeyLoginBegin", new { username = owner.Stored.Username });
        var credential = key.Assert(options, advanceCounter: false);
        var token = VaultFixture.FormTokenOf(await browser.Client.GetStringAsync("/Account/Login"), "/Account/Login");
        var before = await AuditTrail.HeadAsync(vault);
        using var first = AssertionRequest(token, credential);
        using var second = AssertionRequest(token, credential);

        var responses = await Task.WhenAll(browser.Client.SendAsync(first), browser.Client.SendAsync(second));
        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<JsonElement>()));

        Assert.Single(bodies, body => body.GetProperty("success").GetBoolean());
        Assert.Single(bodies, body => !body.GetProperty("success").GetBoolean());
        Assert.Equal([AuditAction.PasskeyLogin], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
    }

    private static HttpRequestMessage AssertionRequest(string token, object credential)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/PasskeyLoginComplete") { Content = JsonContent.Create(new { credential }) };
        request.Headers.Add("RequestVerificationToken", token);
        return request;
    }
}
