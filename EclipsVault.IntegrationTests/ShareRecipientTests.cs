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
        var details = await SecretSharing.CreateSecretAsync(issuer.Client, "it-share-confidential-secret", sensitivity: "Confidential");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretDetailsApi.ShareAsync(issuer.Client, details, "it-share-under-cleared");

        var problem = await SecretsApi.AssertProblemAsync(response, 409, "share_refused");
        Assert.Equal("That account's clearance is below this secret's sensitivity, so a share would not let them open it.",
            problem.GetProperty("title").GetString());
        Assert.False(await SecretSharing.HasAnyShareAsync(vault, details));
        Assert.Empty(await AuditTrail.ShareRowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Sharing_again_after_a_share_expired_replaces_it_with_one_shared_row_and_no_revoked_row()
    {
        var issuer = await vault.AdminClientAsync();
        var recipient = await vault.SignInNewAccountAsync("it-share-expired-recipient", IPAddress.Parse("198.51.100.151"), project: "SHARE-RULE");
        var details = await SecretSharing.CreateSecretAsync(issuer, "it-share-expired-secret");
        Assert.Equal(HttpStatusCode.NoContent, (await SecretDetailsApi.ShareAsync(issuer, details, "it-share-expired-recipient", ttlDays: 1)).StatusCode);
        var expired = await SecretSharing.ShareOfAsync(vault, details);
        await SecretSharing.ExpireShareAsync(vault, expired.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await SecretDetailsApi.GetAsync(recipient.Client, details)).StatusCode);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretDetailsApi.ShareAsync(issuer, details, "it-share-expired-recipient");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = Assert.Single(await AuditTrail.ShareRowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.SecretShared, row.Action);
        Assert.Equal(expired.SecretId, row.ResourceId);
        Assert.Equal("it-share-expired-secret", row.ResourceName);
        Assert.Equal("Shared with 'it-share-expired-recipient', replacing an expired share", row.Details);
        var replacement = await SecretSharing.ShareOfAsync(vault, details);
        Assert.NotEqual(expired.Id, replacement.Id);
        Assert.Null(replacement.ExpiresAtUtc);
        Assert.Equal(HttpStatusCode.OK, (await SecretDetailsApi.GetAsync(recipient.Client, details)).StatusCode);
    }
}
