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
    public async Task The_outbox_reports_a_provisioning_email_caused_by_this_request()
    {
        var admin = await vault.AdminClientAsync();
        var earlier = await UserAccounts.CreateAsync(admin, "it-notifications-earlier", "Outbox", "Earlier");
        Assert.Equal(HttpStatusCode.Created, earlier.StatusCode);
        var earlierAddress = (await earlier.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString();
        var created = await UserAccounts.CreateAsync(admin, "it-notifications-provisioned", "Outbox", "Reader");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var address = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("email").GetString();

        var response = await admin.GetAsync("/api/ui/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["delivery", "outbox", "smtpTarget"], ProfileApi.NamesOf(body));
        Assert.Equal("log", body.GetProperty("delivery").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("smtpTarget").ValueKind);
        var outbox = body.GetProperty("outbox").EnumerateArray().ToList();
        Assert.InRange(outbox.Count, 1, 200);
        Assert.Equal([address, earlierAddress], outbox.Select(email => email.GetProperty("to").GetString())
            .Where(to => to == address || to == earlierAddress));
        var email = Assert.Single(outbox, email => email.GetProperty("to").GetString() == address);
        Assert.Equal(["body", "error", "eventType", "sentAtUtc", "status", "subject", "to", "transport"], ProfileApi.NamesOf(email));
        Assert.Equal("Your EclipsVault account is ready", email.GetProperty("subject").GetString());
        Assert.Equal("Hello Outbox Reader,\n\nAn EclipsVault account has been created for you.\nSign in with the username 'it-notifications-provisioned' (or this email) and the password an administrator gave you; you'll enrol an authenticator on first sign-in.\n\n— EclipsVault", email.GetProperty("body").GetString());
        Assert.Equal("UserProvisioned", email.GetProperty("eventType").GetString());
        Assert.Equal("Log", email.GetProperty("transport").GetString());
        Assert.Equal("sent", email.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, email.GetProperty("error").ValueKind);
        using var scope = vault.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailLogs.SingleAsync(row => row.ToAddress == address);
        Assert.Equal(stored.CreatedAtUtc, email.GetProperty("sentAtUtc").GetDateTimeOffset());
    }
}
