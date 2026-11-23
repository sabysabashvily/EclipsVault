using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class NotificationsApiTests(VaultFixture vault)
{
    [Fact]
    public async Task The_outbox_reports_a_security_notice_caused_by_this_request()
    {
        var earlier = await ResetAuthenticatorAsync("it-notifications-earlier", "100.65.180.1");
        var owner = await ResetAuthenticatorAsync("it-notifications-reset", "100.65.180.3");
        var admin = await vault.AdminClientAsync();

        var response = await admin.GetAsync("/api/ui/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["delivery", "outbox", "smtpTarget"], ProfileApi.NamesOf(body));
        Assert.Equal("smtp", body.GetProperty("delivery").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("smtpTarget").ValueKind);
        var outbox = body.GetProperty("outbox").EnumerateArray().ToList();
        Assert.InRange(outbox.Count, 1, 200);
        Assert.Equal([owner.Stored.Email, earlier.Stored.Email], outbox.Select(email => email.GetProperty("to").GetString())
            .Where(to => to == owner.Stored.Email || to == earlier.Stored.Email));
        var email = Assert.Single(outbox, email => email.GetProperty("to").GetString() == owner.Stored.Email);
        Assert.Equal(["body", "error", "eventType", "sentAtUtc", "status", "subject", "to", "transport"], ProfileApi.NamesOf(email));
        Assert.Equal("Your EclipsVault security settings changed", email.GetProperty("subject").GetString());
        Assert.Equal("A sensitive change was completed on your EclipsVault account. If this wasn't you, contact an administrator immediately.", email.GetProperty("body").GetString());
        Assert.Equal("ResetAuthenticator", email.GetProperty("eventType").GetString());
        Assert.Equal("Smtp", email.GetProperty("transport").GetString());
        Assert.Equal("sent", email.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, email.GetProperty("error").ValueKind);
        using var scope = vault.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailLogs.SingleAsync(row => row.ToAddress == owner.Stored.Email);
        Assert.Equal(stored.CreatedAtUtc, email.GetProperty("sentAtUtc").GetDateTimeOffset());
    }

    /// <summary>A new account that resets its own authenticator, which sends it a security notice through the outbox.</summary>
    private async Task<ProfileAccount> ResetAuthenticatorAsync(string username, string address)
    {
        var owner = await ProfileAccount.CreateAsync(vault, username, address);
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner, new { kind = "ResetAuthenticator" });
        Assert.Equal(HttpStatusCode.NoContent, (await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post,
            "/api/ui/profile/authenticator/reset", new { operationId })).StatusCode);
        return owner;
    }
}
