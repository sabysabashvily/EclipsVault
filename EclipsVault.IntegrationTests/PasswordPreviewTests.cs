using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class PasswordPreviewTests(VaultFixture vault)
{
    [Theory]
    [InlineData("password", true)]
    [InlineData("Preview-Unique-Password-89!", false)]
    [InlineData("", false)]
    public async Task Breach_preview_returns_only_a_verdict_without_audit_or_input_disclosure(string password, bool compromised)
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-preview-" + compromised + password.Length, "100.65.89." + (password.Length + 1));
        var before = await AuditTrail.HeadAsync(vault);

        var response = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/passwords/check-breached", new { password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var verdict = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["compromised"], ProfileApi.NamesOf(verdict));
        Assert.Equal(compromised, verdict.GetProperty("compromised").GetBoolean());
        Assert.Empty(await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, owner.Id, "/api/ui/passwords/check-breached", "Preview-Unique-Password-89!");
    }

    [Fact]
    public async Task New_user_gets_a_verdict_with_the_UI_antiforgery_token()
    {
        var admin = await vault.AdminClientAsync();
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);
        using var check = new HttpRequestMessage(HttpMethod.Post, "/api/ui/passwords/check-breached")
        {
            Content = JsonContent.Create(new { password = "000000000000" }),
        };
        check.Headers.Add("RequestVerificationToken", token);

        var response = await admin.SendAsync(check);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("compromised").GetBoolean());
    }
}
