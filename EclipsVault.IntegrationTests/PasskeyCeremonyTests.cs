using System.Net;
using System.Net.Http.Json;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class PasskeyCeremonyTests(VaultFixture vault)
{
    [Fact]
    public async Task A_counterless_passkey_assertion_presented_twice_signs_in_only_once()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-legacy-key-once", "100.72.87.1");
        using var key = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner.Browser, key, "Counterless");
        var browser = vault.CreateSession(source: IPAddress.Parse("100.72.87.2"));
        var options = await SignInProof.PostPasskeyAsync(browser.Client, "/api/ui/auth/passkey/begin", new { });
        var credential = key.Assert(options.GetProperty("publicKey"), advanceCounter: false);
        var token = await SignInDocument.TokenAsync(browser.Client);
        var before = await AuditTrail.HeadAsync(vault);
        using var first = AssertionRequest(token, credential);
        using var second = AssertionRequest(token, credential);

        var responses = await Task.WhenAll(browser.Client.SendAsync(first), browser.Client.SendAsync(second));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode != HttpStatusCode.OK);
        Assert.Equal([AuditAction.SignInFactorVerified, AuditAction.PasskeyLogin], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
    }

    /// <summary>Profile registers only discoverable passkeys, the only kind that can sign in.</summary>
    [Fact]
    public async Task Profile_asks_for_a_discoverable_passkey()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-f4-resident-key", "100.72.87.5");
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "AddPasskey", passkeyNickname = "Resident" });

        var options = await ProfileApi.PasskeyPromptAsync(owner.Browser, "/api/ui/profile/passkeys/registration/begin", new { operationId });

        Assert.Equal("required", options.GetProperty("authenticatorSelection").GetProperty("residentKey").GetString());
    }

    private static HttpRequestMessage AssertionRequest(string token, object credential)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ui/auth/passkey/complete") { Content = JsonContent.Create(new { credential }) };
        request.Headers.Add("RequestVerificationToken", token);
        return request;
    }
}
