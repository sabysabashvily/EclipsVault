using System.Net;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The sharing panel refuses a recipient a share could not help, and replaces a recipient's
/// expired share instead of being blocked by it.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class ShareRecipientTests(VaultFixture vault)
{
    [Fact]
    public async Task Sharing_with_an_account_whose_clearance_is_below_the_sensitivity_is_refused_with_the_reason()
    {
        // Only a trusted address may open a Confidential secret, and the issuer must open it to share it.
        var issuer = await vault.SignInNewAccountAsync("it-share-cleared-issuer", IPAddress.Parse("10.97.0.1"), ClearanceLevel.TopSecret);
        await vault.SignInNewAccountAsync("it-share-under-cleared", IPAddress.Parse("198.51.100.150"), project: "SHARE-RULE");
        var details = await LegacySharing.CreateSecretAsync(issuer.Client, "it-share-confidential-secret", sensitivity: "Confidential");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacySharing.ShareAsync(issuer.Client, details, "it-share-under-cleared");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(details, response.Headers.Location?.OriginalString);
        Assert.Contains("That account's clearance is below this secret's sensitivity, so a share would not let them open it.",
            WebUtility.HtmlDecode(await issuer.Client.GetStringAsync(details)));
        Assert.False(await LegacySharing.HasAnyShareAsync(vault, details));
        Assert.Empty(await AuditTrail.ShareRowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Sharing_again_after_a_share_expired_replaces_it_with_one_shared_row_and_no_revoked_row()
    {
        var issuer = await vault.AdminClientAsync();
        var recipient = await vault.SignInNewAccountAsync("it-share-expired-recipient", IPAddress.Parse("198.51.100.151"), project: "SHARE-RULE");
        var details = await LegacySharing.CreateSecretAsync(issuer, "it-share-expired-secret");
        Assert.Equal(HttpStatusCode.Redirect, (await LegacySharing.ShareAsync(issuer, details, "it-share-expired-recipient", ttlDays: "1")).StatusCode);
        var expired = await LegacySharing.ShareOfAsync(vault, details);
        await LegacySharing.ExpireShareAsync(vault, expired.Id);
        Assert.StartsWith("/Account/Denied?", (await recipient.Client.GetAsync(details)).Headers.Location?.OriginalString);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacySharing.ShareAsync(issuer, details, "it-share-expired-recipient");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(details, response.Headers.Location?.OriginalString);
        var row = Assert.Single(await AuditTrail.ShareRowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.SecretShared, row.Action);
        Assert.Equal(expired.SecretId, row.ResourceId);
        Assert.Equal("it-share-expired-secret", row.ResourceName);
        Assert.Equal("Shared with 'it-share-expired-recipient', replacing an expired share", row.Details);
        var replacement = await LegacySharing.ShareOfAsync(vault, details);
        Assert.NotEqual(expired.Id, replacement.Id);
        Assert.Null(replacement.ExpiresAtUtc);
        Assert.Equal(HttpStatusCode.OK, (await recipient.Client.GetAsync(details)).StatusCode);
    }
}
