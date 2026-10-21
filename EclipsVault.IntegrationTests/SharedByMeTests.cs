using System.Net;
using System.Net.Http.Json;
using EclipsVault.Web.Models.Ui;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class SharedByMeTests(VaultFixture vault)
{
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
}
