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
        Assert.Empty(await LegacyProfile.EventsAfterAsync(vault, before, owner.Id));
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, owner.Id, "/api/ui/passwords/check-breached", "Preview-Unique-Password-89!");
    }

    [Fact]
    public async Task Legacy_password_fields_use_the_session_only_breach_preview()
    {
        var admin = await vault.AdminClientAsync();

        var create = await admin.GetStringAsync("/Admin/CreateUser");
        var change = await admin.GetStringAsync("/Profile/ChangePassword");

        Assert.Contains("data-breach-check=\"/api/ui/passwords/check-breached\"", create);
        Assert.Contains("data-breach-check=\"/api/ui/passwords/check-breached\"", change);
    }
}
