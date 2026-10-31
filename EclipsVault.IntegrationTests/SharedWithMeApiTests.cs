using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models.Ui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Shared with me lists only the shares its recipient could list on the Secrets page (ADR-0027).
/// A trusted address is in 10.0.0.0/8; 100.104.0.0/16 is not. Each account signs in from its own
/// address, so no test spends another's sign-in budget.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SharedWithMeApiTests(VaultFixture vault)
{
    [Fact]
    public async Task Shared_with_me_refuses_anonymous_and_working_api_key_callers()
    {
        var client = vault.CreateClient();
        await UiApiFailureContractTests.AssertProblemAsync(await client.GetAsync("/api/ui/shared-with-me"), 401, "unauthenticated");
        var key = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-swm-key");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/secrets")).StatusCode);

        await UiApiFailureContractTests.AssertProblemAsync(await client.GetAsync("/api/ui/shared-with-me"), 401, "unauthenticated");
    }

    [Fact]
    public async Task The_recipients_shares_are_returned_by_name_as_page_data()
    {
        var issuer = await vault.SignInNewAccountAsync("it-swm-shape-issuer", IPAddress.Parse("10.104.0.1"), ClearanceLevel.TopSecret);
        var recipient = await vault.SignInNewAccountAsync("it-swm-shape", IPAddress.Parse("10.104.0.2"), ClearanceLevel.Secret, "IT-SWM-SHAPE");
        var staging = await LegacySharing.CreateSecretAsync(issuer.Client, "it-swm-shape-staging", sensitivity: "Secret", environment: "Staging");
        var development = await LegacySharing.CreateSecretAsync(issuer.Client, "it-swm-shape-development");
        await LegacySharing.ShareAsync(issuer.Client, staging, "it-swm-shape", ttlDays: "7");
        await LegacySharing.ShareAsync(issuer.Client, development, "it-swm-shape");
        var expiringShare = await LegacySharing.ShareOfAsync(vault, staging);

        var response = await recipient.Client.GetAsync("/api/ui/shared-with-me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["shares"], NamesOf(document.RootElement));
        var shares = document.RootElement.GetProperty("shares").EnumerateArray().ToArray();
        Assert.Equal(2, shares.Length);
        Assert.All(shares, share => Assert.Equal(
            ["accessUntilUtc", "environment", "name", "project", "secretId", "sensitivity", "sensitivityLabel", "sharedBy"], NamesOf(share)));
        Assert.Equal(LegacySharing.SecretIdOf(development), shares[0].GetProperty("secretId").GetGuid());
        Assert.Equal("it-swm-shape-development", shares[0].GetProperty("name").GetString());
        Assert.Equal("GLOBAL", shares[0].GetProperty("project").GetString());
        Assert.Equal("Development", shares[0].GetProperty("environment").GetString());
        Assert.Equal(1, shares[0].GetProperty("sensitivity").GetInt32());
        Assert.Equal("Internal", shares[0].GetProperty("sensitivityLabel").GetString());
        Assert.Equal("it-swm-shape-issuer", shares[0].GetProperty("sharedBy").GetString());
        Assert.Equal(JsonValueKind.Null, shares[0].GetProperty("accessUntilUtc").ValueKind);
        Assert.Equal(LegacySharing.SecretIdOf(staging), shares[1].GetProperty("secretId").GetGuid());
        Assert.Equal("it-swm-shape-staging", shares[1].GetProperty("name").GetString());
        Assert.Equal("GLOBAL", shares[1].GetProperty("project").GetString());
        Assert.Equal("Staging", shares[1].GetProperty("environment").GetString());
        Assert.Equal(3, shares[1].GetProperty("sensitivity").GetInt32());
        Assert.Equal("Secret", shares[1].GetProperty("sensitivityLabel").GetString());
        Assert.Equal("it-swm-shape-issuer", shares[1].GetProperty("sharedBy").GetString());
        Assert.Equal(expiringShare.ExpiresAtUtc!.Value.UtcDateTime, shares[1].GetProperty("accessUntilUtc").GetDateTime());
    }

    [Fact]
    public async Task Two_recipients_see_only_their_own_shares_despite_injected_parameters()
    {
        var first = await vault.SignInNewAccountAsync("it-swm-scope-a", IPAddress.Parse("10.104.0.3"), project: "IT-SWM-A");
        var second = await vault.SignInNewAccountAsync("it-swm-scope-b", IPAddress.Parse("10.104.0.4"), project: "IT-SWM-B");
        var firstId = SessionApi.PrincipalOf(vault, first).GetUserId();
        var secondId = SessionApi.PrincipalOf(vault, second).GetUserId();
        var admin = await vault.AdminClientAsync();
        await LegacySharing.ShareAsync(admin, await LegacySharing.CreateSecretAsync(admin, "it-swm-scope-secret-a"), "it-swm-scope-a");
        await LegacySharing.ShareAsync(admin, await LegacySharing.CreateSecretAsync(admin, "it-swm-scope-secret-b"), "it-swm-scope-b");
        var claimingSecond = $"?userId={secondId}&recipientId={secondId}&granteeUserId={secondId}&accountId={secondId}&username=it-swm-scope-b&recipient=it-swm-scope-b";
        var claimingFirst = $"?userId={firstId}&recipientId={firstId}&granteeUserId={firstId}&accountId={firstId}&username=it-swm-scope-a&recipient=it-swm-scope-a";

        var firstShares = await first.Client.GetFromJsonAsync<SharedWithMeResponse>("/api/ui/shared-with-me" + claimingSecond);
        var secondShares = await second.Client.GetFromJsonAsync<SharedWithMeResponse>("/api/ui/shared-with-me" + claimingFirst);

        Assert.Equal("it-swm-scope-secret-a", Assert.Single(firstShares!.Shares).Name);
        Assert.Equal("it-swm-scope-secret-b", Assert.Single(secondShares!.Shares).Name);
    }

    [Fact]
    public async Task A_share_to_a_recipient_whose_clearance_is_too_low_is_left_out()
    {
        var recipient = await vault.SignInNewAccountAsync("it-swm-under-cleared", IPAddress.Parse("10.104.0.5"), project: "IT-SWM-CLEARANCE");
        var admin = await vault.AdminClientAsync();
        var confidential = await LegacySharing.CreateSecretAsync(admin, "it-swm-under-cleared-confidential", sensitivity: "Confidential");
        var internalSecret = await LegacySharing.CreateSecretAsync(admin, "it-swm-under-cleared-internal");
        await LegacySharing.InsertShareAsync(vault, confidential, "it-swm-under-cleared");
        await LegacySharing.ShareAsync(admin, internalSecret, "it-swm-under-cleared");

        Assert.Equal(["it-swm-under-cleared-internal"], await ListedNamesAsync(recipient));
    }

    [Fact]
    public async Task A_share_that_needs_a_trusted_network_is_left_out_off_the_network()
    {
        var issuer = await vault.SignInNewAccountAsync("it-swm-off-network-issuer", IPAddress.Parse("10.104.0.6"), ClearanceLevel.TopSecret);
        var recipient = await vault.SignInNewAccountAsync(
            "it-swm-off-network", IPAddress.Parse("100.104.0.1"), ClearanceLevel.Elevated, "IT-SWM-NETWORK");
        var confidential = await LegacySharing.CreateSecretAsync(issuer.Client, "it-swm-off-network-confidential", sensitivity: "Confidential");
        var internalSecret = await LegacySharing.CreateSecretAsync(issuer.Client, "it-swm-off-network-internal");
        Assert.Equal(HttpStatusCode.Redirect, (await LegacySharing.ShareAsync(issuer.Client, confidential, "it-swm-off-network")).StatusCode);
        Assert.True(await LegacySharing.HasAnyShareAsync(vault, confidential));
        await LegacySharing.ShareAsync(issuer.Client, internalSecret, "it-swm-off-network");

        Assert.Equal(["it-swm-off-network-internal"], await ListedNamesAsync(recipient));
    }

    [Fact]
    public async Task Expired_shares_and_shares_on_shredded_or_expired_secrets_are_left_out()
    {
        var recipient = await vault.SignInNewAccountAsync("it-swm-lifetime", IPAddress.Parse("10.104.0.7"), project: "IT-SWM-LIFETIME");
        var admin = await vault.AdminClientAsync();
        var expiredShare = await LegacySharing.CreateSecretAsync(admin, "it-swm-lifetime-expired-share");
        var shredded = await LegacySharing.CreateSecretAsync(admin, "it-swm-lifetime-shredded");
        var expiredSecret = await LegacySharing.CreateSecretAsync(admin, "it-swm-lifetime-expired-secret");
        var live = await LegacySharing.CreateSecretAsync(admin, "it-swm-lifetime-live");
        await LegacySharing.ShareAsync(admin, expiredShare, "it-swm-lifetime");
        await LegacySharing.ShareAsync(admin, shredded, "it-swm-lifetime");
        await LegacySharing.ShareAsync(admin, expiredSecret, "it-swm-lifetime");
        await LegacySharing.ShareAsync(admin, live, "it-swm-lifetime");

        await LegacySharing.ExpireShareAsync(vault, (await LegacySharing.ShareOfAsync(vault, expiredShare)).Id);
        using (var scope = vault.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            (await db.Secrets.SingleAsync(secret => secret.Id == LegacySharing.SecretIdOf(shredded))).Shred(DateTimeOffset.UtcNow);
            (await db.Secrets.SingleAsync(secret => secret.Id == LegacySharing.SecretIdOf(expiredSecret))).ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(["it-swm-lifetime-live"], await ListedNamesAsync(recipient));
    }

    [Fact]
    public async Task A_deleted_accounts_cookie_cannot_list_shares()
    {
        var source = IPAddress.Parse("10.104.0.8");
        var session = await vault.SignInNewAccountAsync("it-swm-deleted", source);
        await vault.DeleteAccountAsync("it-swm-deleted");

        await UiApiFailureContractTests.AssertProblemAsync(await session.Client.GetAsync("/api/ui/shared-with-me"), 401, "unauthenticated");
    }

    [Fact]
    public async Task A_cookie_without_its_account_id_cannot_list_shares()
    {
        var source = IPAddress.Parse("10.104.0.9");
        var session = await vault.SignInNewAccountAsync("it-swm-no-id", source);
        var cookie = SessionApi.WithoutAccountId(vault, session);

        await UiApiFailureContractTests.AssertProblemAsync(await vault.CreateSession(cookie, source).Client.GetAsync("/api/ui/shared-with-me"), 401, "unauthenticated");
    }

    private static async Task<string[]> ListedNamesAsync(VaultSession recipient)
        => (await recipient.Client.GetFromJsonAsync<SharedWithMeResponse>("/api/ui/shared-with-me"))!.Shares
            .Select(share => share.Name)
            .ToArray();

    private static string[] NamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}

/// <summary>Production secrets open only inside the production window, so outside it their shares are left out too.</summary>
[Collection(ClosedProductionWindowCollection.Name)]
public sealed class SharedWithMeProductionWindowTests(ClosedProductionWindowFixture fixture)
{
    [Fact]
    public async Task A_share_on_a_production_secret_is_left_out_while_the_window_is_closed()
    {
        var vault = fixture.Vault;
        var recipient = await vault.SignInNewAccountAsync("it-swm-window", IPAddress.Parse("10.104.0.10"), project: "IT-SWM-WINDOW");
        var admin = await vault.AdminClientAsync();
        var production = await LegacySharing.CreateSecretAsync(admin, "it-swm-window-production", environment: "Production");
        var development = await LegacySharing.CreateSecretAsync(admin, "it-swm-window-development");
        await LegacySharing.InsertShareAsync(vault, production, "it-swm-window");
        await LegacySharing.ShareAsync(admin, development, "it-swm-window");

        var listed = await recipient.Client.GetFromJsonAsync<SharedWithMeResponse>("/api/ui/shared-with-me");

        Assert.Equal("it-swm-window-development", Assert.Single(listed!.Shares).Name);
    }
}
