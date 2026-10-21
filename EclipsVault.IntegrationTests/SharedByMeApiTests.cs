using System.Globalization;
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

[Collection(VaultCollection.Name)]
public sealed class SharedByMeApiTests(VaultFixture vault)
{
    [Theory]
    [InlineData("GET", "/api/ui/shared-by-me")]
    [InlineData("DELETE", "/api/ui/shared-by-me/11111111-1111-1111-1111-111111111111")]
    public async Task Each_share_endpoint_refuses_anonymous_and_working_api_key_callers(string method, string path)
    {
        var client = vault.CreateClient();
        await UiApiFailureContractTests.AssertProblemAsync(await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), 401, "unauthenticated");
        var key = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), $"it-shares-key-{method}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/secrets")).StatusCode);

        await UiApiFailureContractTests.AssertProblemAsync(await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), 401, "unauthenticated");
    }

    [Fact]
    public async Task The_issuers_live_shares_are_returned_as_page_data()
    {
        var issuer = await vault.SignInNewAccountAsync("it-shares-shape", IPAddress.Parse("192.0.2.80"));
        await vault.SignInNewAccountAsync("it-shares-shape-recipient", IPAddress.Parse("192.0.2.81"));
        var admin = await vault.AdminClientAsync();
        var older = await LegacySharing.CreateSecretAsync(admin, "it-shares-older");
        var newer = await LegacySharing.CreateSecretAsync(admin, "it-shares-newer");
        await LegacySharing.ShareAsync(issuer.Client, older, "it-shares-shape-recipient");
        await LegacySharing.ShareAsync(issuer.Client, newer, "it-shares-shape-recipient", ttlDays: "7");
        var olderShare = await LegacySharing.ShareOfAsync(vault, older);
        var newerShare = await LegacySharing.ShareOfAsync(vault, newer);

        var response = await issuer.Client.GetAsync("/api/ui/shared-by-me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["shares"], NamesOf(document.RootElement));
        var shares = document.RootElement.GetProperty("shares").EnumerateArray().ToArray();
        Assert.Equal(2, shares.Length);
        Assert.All(shares, share => Assert.Equal(["createdAtUtc", "expiresAtUtc", "recipient", "secretName", "shareId"], NamesOf(share)));
        Assert.Equal(newerShare.Id, shares[0].GetProperty("shareId").GetGuid());
        Assert.Equal("it-shares-newer", shares[0].GetProperty("secretName").GetString());
        Assert.Equal("it-shares-shape-recipient", shares[0].GetProperty("recipient").GetString());
        Assert.Equal(newerShare.CreatedAtUtc.UtcDateTime, shares[0].GetProperty("createdAtUtc").GetDateTime());
        Assert.Equal(newerShare.ExpiresAtUtc!.Value.UtcDateTime, shares[0].GetProperty("expiresAtUtc").GetDateTime());
        Assert.Equal(olderShare.Id, shares[1].GetProperty("shareId").GetGuid());
        Assert.Equal("it-shares-older", shares[1].GetProperty("secretName").GetString());
        Assert.Equal("it-shares-shape-recipient", shares[1].GetProperty("recipient").GetString());
        Assert.Equal(olderShare.CreatedAtUtc.UtcDateTime, shares[1].GetProperty("createdAtUtc").GetDateTime());
        Assert.Equal(JsonValueKind.Null, shares[1].GetProperty("expiresAtUtc").ValueKind);
    }

    [Fact]
    public async Task Two_issuers_list_and_revoke_only_their_own_shares_despite_injected_parameters()
    {
        var issuer = await vault.SignInNewAccountAsync("it-shares-scope-a", IPAddress.Parse("192.0.2.82"));
        var otherIssuer = await vault.SignInNewAccountAsync("it-shares-scope-b", IPAddress.Parse("192.0.2.83"));
        await vault.SignInNewAccountAsync("it-shares-scope-recipient", IPAddress.Parse("192.0.2.84"), project: "SHARES-SCOPE");
        var issuerId = SessionApi.PrincipalOf(vault, issuer).GetUserId();
        var otherIssuerId = SessionApi.PrincipalOf(vault, otherIssuer).GetUserId();
        var admin = await vault.AdminClientAsync();
        var issuedSecret = await LegacySharing.CreateSecretAsync(admin, "it-shares-scope-secret-a");
        var otherIssuedSecret = await LegacySharing.CreateSecretAsync(admin, "it-shares-scope-secret-b");
        await LegacySharing.ShareAsync(issuer.Client, issuedSecret, "it-shares-scope-recipient");
        await LegacySharing.ShareAsync(otherIssuer.Client, otherIssuedSecret, "it-shares-scope-recipient");
        var issuedShare = await LegacySharing.ShareOfAsync(vault, issuedSecret);
        var otherIssuedShare = await LegacySharing.ShareOfAsync(vault, otherIssuedSecret);
        var claimingOtherIssuer = $"?issuerId={otherIssuerId}&issuerUserId={otherIssuerId}&userId={otherIssuerId}&accountId={otherIssuerId}&username=it-shares-scope-b&issuer=it-shares-scope-b";
        var claimingIssuer = $"?issuerId={issuerId}&issuerUserId={issuerId}&userId={issuerId}&accountId={issuerId}&username=it-shares-scope-a&issuer=it-shares-scope-a";
        var before = await LegacySharing.AuditHeadAsync(vault);

        var issuedShares = await issuer.Client.GetFromJsonAsync<SharedByMeResponse>("/api/ui/shared-by-me" + claimingOtherIssuer);
        var otherIssuedShares = await otherIssuer.Client.GetFromJsonAsync<SharedByMeResponse>("/api/ui/shared-by-me" + claimingIssuer);
        Assert.Equal(issuedShare.Id, Assert.Single(issuedShares!.Shares).ShareId);
        Assert.Equal(otherIssuedShare.Id, Assert.Single(otherIssuedShares!.Shares).ShareId);
        var foreign = await SessionApi.WriteAsync(issuer, HttpMethod.Delete, $"/api/ui/shared-by-me/{otherIssuedShare.Id}" + claimingOtherIssuer,
            JsonContent.Create(new { issuerId = otherIssuerId, issuerUserId = otherIssuerId, accountId = otherIssuerId, userId = otherIssuerId, username = "it-shares-scope-b", issuer = "it-shares-scope-b" }));
        var missing = await SessionApi.WriteAsync(otherIssuer, HttpMethod.Delete, "/api/ui/shared-by-me/11111111-1111-1111-1111-111111111111");
        await UiApiFailureContractTests.AssertProblemAsync(foreign, 404, "share_not_found");
        await UiApiFailureContractTests.AssertProblemAsync(missing, 404, "share_not_found");
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());
        using var problem = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync());
        Assert.Equal("That grant no longer exists, or it wasn't yours to revoke.", problem.RootElement.GetProperty("title").GetString());
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));

        Assert.Equal(HttpStatusCode.NoContent, (await SessionApi.WriteAsync(issuer, HttpMethod.Delete, $"/api/ui/shared-by-me/{issuedShare.Id}" + claimingOtherIssuer)).StatusCode);
        Assert.Empty((await issuer.Client.GetFromJsonAsync<SharedByMeResponse>("/api/ui/shared-by-me"))!.Shares);
        Assert.Equal(otherIssuedShare.Id, Assert.Single((await otherIssuer.Client.GetFromJsonAsync<SharedByMeResponse>("/api/ui/shared-by-me"))!.Shares).ShareId);
        Assert.Equal(HttpStatusCode.NoContent, (await SessionApi.WriteAsync(otherIssuer, HttpMethod.Delete, $"/api/ui/shared-by-me/{otherIssuedShare.Id}" + claimingIssuer)).StatusCode);
        Assert.Empty((await otherIssuer.Client.GetFromJsonAsync<SharedByMeResponse>("/api/ui/shared-by-me"))!.Shares);
    }

    [Fact]
    public async Task A_revocation_records_who_revoked_which_secret_for_whom()
    {
        var issuer = await vault.SignInNewAccountAsync("it-shares-audit", IPAddress.Parse("192.0.2.97"));
        await vault.SignInNewAccountAsync("it-shares-audit-recipient", IPAddress.Parse("192.0.2.98"));
        var issuerId = SessionApi.PrincipalOf(vault, issuer).GetUserId();
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-shares-audit-secret");
        await LegacySharing.ShareAsync(issuer.Client, details, "it-shares-audit-recipient");
        var share = await LegacySharing.ShareOfAsync(vault, details);
        var before = await LegacySharing.AuditHeadAsync(vault);

        var revoked = await SessionApi.WriteAsync(issuer, HttpMethod.Delete, $"/api/ui/shared-by-me/{share.Id}");

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.True(revoked.Headers.CacheControl?.NoStore);
        Assert.Empty(await revoked.Content.ReadAsStringAsync());
        var row = Assert.Single(await LegacySharing.AuditRowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.SecretShareRevoked, row.Action);
        Assert.Equal(issuerId, row.UserId);
        Assert.Equal("it-shares-audit", row.Username);
        Assert.Equal("192.0.2.97", row.SourceIp);
        Assert.Equal("Secret", row.ResourceType);
        Assert.Equal(share.SecretId, row.ResourceId);
        Assert.Equal("it-shares-audit-secret", row.ResourceName);
        Assert.Equal("Revoked access for 'it-shares-audit-recipient'", row.Details);
    }

    [Fact]
    public async Task Administrator_responses_and_revocation_have_no_wider_issuer_scope()
    {
        var admin = await vault.SignInNewAccountAsync("it-shares-admin", IPAddress.Parse("192.0.2.85"), ClearanceLevel.TopSecret);
        var other = await vault.SignInNewAccountAsync("it-shares-admin-other", IPAddress.Parse("192.0.2.86"));
        await vault.SignInNewAccountAsync("it-shares-admin-recipient", IPAddress.Parse("192.0.2.87"));
        var owned = await LegacySharing.CreateSecretAsync(admin.Client, "it-shares-admin-owned");
        var foreign = await LegacySharing.CreateSecretAsync(admin.Client, "it-shares-admin-foreign");
        await LegacySharing.ShareAsync(admin.Client, owned, "it-shares-admin-recipient");
        await LegacySharing.ShareAsync(other.Client, foreign, "it-shares-admin-recipient");
        var ownedShare = await LegacySharing.ShareOfAsync(vault, owned);
        var foreignShare = await LegacySharing.ShareOfAsync(vault, foreign);
        var before = await LegacySharing.AuditHeadAsync(vault);

        var shown = await admin.Client.GetFromJsonAsync<SharedByMeResponse>("/api/ui/shared-by-me");
        Assert.Equal(ownedShare.Id, Assert.Single(shown!.Shares).ShareId);
        await UiApiFailureContractTests.AssertProblemAsync(await SessionApi.WriteAsync(admin, HttpMethod.Delete, $"/api/ui/shared-by-me/{foreignShare.Id}"), 404, "share_not_found");
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
        Assert.Equal(HttpStatusCode.NoContent, (await SessionApi.WriteAsync(admin, HttpMethod.Delete, $"/api/ui/shared-by-me/{ownedShare.Id}")).StatusCode);
    }

    [Fact]
    public async Task An_issuer_who_cannot_open_the_secret_can_still_list_and_revoke_its_share()
    {
        var account = await SessionAccount.CreateAsync(vault, "it-shares-lost-access", "192.0.2.88");
        var recipient = await vault.SignInNewAccountAsync("it-shares-lost-recipient", IPAddress.Parse("192.0.2.89"), project: "SHARES-LOST");
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-shares-lost-secret");
        await LegacySharing.ShareAsync(account.Browser.Client, details, "it-shares-lost-recipient");
        var share = await LegacySharing.ShareOfAsync(vault, details);
        var issuerId = SessionApi.PrincipalOf(vault, account.Browser).GetUserId();
        var editPath = $"/Admin/EditUser/{issuerId}";
        var edit = await admin.GetStringAsync(editPath);
        var changed = await admin.PostAsync(editPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(edit, editPath),
            ["Id"] = issuerId.ToString(),
            ["Username"] = "it-shares-lost-access",
            ["DisplayName"] = "Integration Test",
            ["Clearance"] = ((int)ClearanceLevel.Standard).ToString(CultureInfo.InvariantCulture),
            ["ProjectKey"] = "SHARES-REASSIGNED",
        }));
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        // The edit revokes the account's sessions, so sign in again to carry the new project. A
        // session's sign-in time is whole seconds and revocation covers its own second, so wait
        // for the next one or the new session is revoked too.
        await Task.Delay(TimeSpan.FromMilliseconds(1_050 - DateTimeOffset.UtcNow.Millisecond));
        var issuer = await account.OpenDeviceAsync(recoveryCodeIndex: 0, "192.0.2.96");
        Assert.StartsWith("/Account/Denied?", (await issuer.Client.GetAsync(details)).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await recipient.Client.GetAsync(details)).StatusCode);

        var list = await issuer.Client.GetFromJsonAsync<SharedByMeResponse>("/api/ui/shared-by-me");
        Assert.Equal(share.Id, Assert.Single(list!.Shares).ShareId);
        Assert.Equal(HttpStatusCode.NoContent, (await SessionApi.WriteAsync(issuer, HttpMethod.Delete, $"/api/ui/shared-by-me/{share.Id}")).StatusCode);
        Assert.Empty((await issuer.Client.GetFromJsonAsync<SharedByMeResponse>("/api/ui/shared-by-me"))!.Shares);
        Assert.StartsWith("/Account/Denied?", (await recipient.Client.GetAsync(details)).Headers.Location?.OriginalString);
    }

    /// <summary>
    /// Deleting an account leaves the shares it issued in place, and its username can then be given to
    /// a new account. A username must never be the key to someone else's shares.
    /// </summary>
    [Fact]
    public async Task A_new_account_with_a_deleted_issuers_username_cannot_see_or_revoke_its_shares()
    {
        const string issuerUsername = "it-share-reused-issuer";
        const string recipientUsername = "it-share-reused-recipient";
        const string secretName = "it-share-reused-secret";
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), secretName);
        // Outside the secret's project, so only the share lets the recipient open it.
        var recipient = await vault.SignInNewAccountAsync(recipientUsername, IPAddress.Parse("198.51.100.120"), project: "SHARE-TEST");
        Assert.StartsWith("/Account/Denied?", (await recipient.Client.GetAsync(details)).Headers.Location?.OriginalString);
        var issuer = await vault.SignInNewAccountAsync(issuerUsername, IPAddress.Parse("198.51.100.121"));
        await LegacySharing.ShareAsync(issuer.Client, details, recipientUsername);
        var issuersList = await issuer.Client.GetFromJsonAsync<SharedByMeResponse>("/api/ui/shared-by-me");
        var share = Assert.Single(issuersList!.Shares);
        Assert.Equal(secretName, share.SecretName);

        await vault.DeleteAccountAsync(issuerUsername);
        var replacement = await vault.SignInNewAccountAsync(issuerUsername, IPAddress.Parse("198.51.100.122"));
        var before = await LegacySharing.AuditHeadAsync(vault);

        var replacementsList = await replacement.Client.GetFromJsonAsync<SharedByMeResponse>("/api/ui/shared-by-me");
        Assert.Empty(replacementsList!.Shares);
        var revoke = await SessionApi.WriteAsync(replacement, HttpMethod.Delete, $"/api/ui/shared-by-me/{share.ShareId}");
        await UiApiFailureContractTests.AssertProblemAsync(revoke, 404, "share_not_found");
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
        Assert.Equal(HttpStatusCode.OK, (await recipient.Client.GetAsync(details)).StatusCode);
        Assert.Contains(secretName, await recipient.Client.GetStringAsync("/Secrets/SharedWithMe"));
    }

    [Fact]
    public async Task Expired_shares_and_shares_on_shredded_secrets_are_omitted()
    {
        var issuer = await vault.SignInNewAccountAsync("it-shares-filter", IPAddress.Parse("192.0.2.90"));
        await vault.SignInNewAccountAsync("it-shares-filter-recipient", IPAddress.Parse("192.0.2.91"));
        var admin = await vault.AdminClientAsync();
        var expired = await LegacySharing.CreateSecretAsync(admin, "it-shares-expired");
        var shredded = await LegacySharing.CreateSecretAsync(admin, "it-shares-shredded");
        var live = await LegacySharing.CreateSecretAsync(admin, "it-shares-live");
        await LegacySharing.ShareAsync(issuer.Client, expired, "it-shares-filter-recipient");
        await LegacySharing.ShareAsync(issuer.Client, shredded, "it-shares-filter-recipient");
        await LegacySharing.ShareAsync(issuer.Client, live, "it-shares-filter-recipient");
        var expiredShare = await LegacySharing.ShareOfAsync(vault, expired);
        var shreddedShare = await LegacySharing.ShareOfAsync(vault, shredded);
        using (var scope = vault.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var share = await db.SecretGrants.SingleAsync(share => share.Id == expiredShare.Id);
            share.ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1);
            var secret = await db.Secrets.SingleAsync(secret => secret.Id == shreddedShare.SecretId);
            secret.Shred(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var list = await issuer.Client.GetFromJsonAsync<SharedByMeResponse>("/api/ui/shared-by-me");

        Assert.Equal("it-shares-live", Assert.Single(list!.Shares).SecretName);
    }

    [Fact]
    public async Task Revocation_without_the_antiforgery_header_keeps_the_share_and_writes_no_audit_row()
    {
        var issuer = await vault.SignInNewAccountAsync("it-shares-csrf", IPAddress.Parse("192.0.2.92"));
        await vault.SignInNewAccountAsync("it-shares-csrf-recipient", IPAddress.Parse("192.0.2.93"));
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-shares-csrf-secret");
        await LegacySharing.ShareAsync(issuer.Client, details, "it-shares-csrf-recipient");
        var share = await LegacySharing.ShareOfAsync(vault, details);
        var before = await LegacySharing.AuditHeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await issuer.Client.DeleteAsync($"/api/ui/shared-by-me/{share.Id}"), 400, "antiforgery_rejected");

        Assert.Equal(share.Id, (await LegacySharing.ShareOfAsync(vault, details)).Id);
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_deleted_accounts_cookie_cannot_list_or_revoke_shares()
    {
        var source = IPAddress.Parse("192.0.2.94");
        var session = await vault.SignInNewAccountAsync("it-shares-deleted", source);
        var cookie = Assert.IsType<Cookie>(session.SessionCookie);
        await vault.DeleteAccountAsync("it-shares-deleted");
        var before = await LegacySharing.AuditHeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await session.Client.GetAsync("/api/ui/shared-by-me"), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(await vault.CreateSession(cookie, source).Client.DeleteAsync("/api/ui/shared-by-me/11111111-1111-1111-1111-111111111111"), 401, "unauthenticated");
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_cookie_without_its_account_id_cannot_list_or_revoke_shares()
    {
        var source = IPAddress.Parse("192.0.2.95");
        var session = await vault.SignInNewAccountAsync("it-shares-no-id", source);
        var cookie = SessionApi.WithoutAccountId(vault, session);
        var before = await LegacySharing.AuditHeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await vault.CreateSession(cookie, source).Client.GetAsync("/api/ui/shared-by-me"), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(await vault.CreateSession(cookie, source).Client.DeleteAsync("/api/ui/shared-by-me/11111111-1111-1111-1111-111111111111"), 401, "unauthenticated");
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }

    private static string[] NamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}
