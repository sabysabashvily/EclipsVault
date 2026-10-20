using System.Globalization;
using System.Net;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class ShareAuditTests(VaultFixture vault)
{
    [Fact]
    public async Task Sharing_from_secret_details_writes_one_audit_row_for_the_secret()
    {
        var issuer = await vault.AdminClientAsync();
        await vault.SignInNewAccountAsync("it-audit-recipient", IPAddress.Parse("198.51.100.130"));
        var details = await LegacySharing.CreateSecretAsync(issuer, "it-audit-shared-secret");
        var before = await LegacySharing.AuditHeadAsync(vault);

        var response = await LegacySharing.ShareAsync(issuer, details, "it-audit-recipient");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(details, response.Headers.Location?.OriginalString);
        var row = Assert.Single(await LegacySharing.ShareAuditRowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.SecretShared, row.Action);
        Assert.Equal("Secret", row.ResourceType);
        Assert.Equal(LegacySharing.SecretIdOf(details), row.ResourceId);
        Assert.Equal("it-audit-shared-secret", row.ResourceName);
        Assert.Equal("Shared with 'it-audit-recipient'", row.Details);
        Assert.Equal(VaultFixture.AdminUsername, row.Username);
        Assert.Null((await LegacySharing.ShareOfAsync(vault, details)).ExpiresAtUtc);
    }

    [Fact]
    public async Task Sharing_with_an_expiry_records_its_utc_expiry_in_one_audit_row()
    {
        var issuer = await vault.AdminClientAsync();
        await vault.SignInNewAccountAsync("it-audit-expiry-recipient", IPAddress.Parse("198.51.100.131"));
        var details = await LegacySharing.CreateSecretAsync(issuer, "it-audit-expiring-secret");
        var before = await LegacySharing.AuditHeadAsync(vault);

        var response = await LegacySharing.ShareAsync(issuer, details, "it-audit-expiry-recipient", ttlDays: "7");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var row = Assert.Single(await LegacySharing.ShareAuditRowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.SecretShared, row.Action);
        Assert.Equal("Secret", row.ResourceType);
        Assert.Equal(LegacySharing.SecretIdOf(details), row.ResourceId);
        Assert.Equal("it-audit-expiring-secret", row.ResourceName);
        var text = Assert.IsType<string>(row.Details);
        Assert.StartsWith("Shared with 'it-audit-expiry-recipient' until ", text);
        var expiry = DateTimeOffset.ParseExact(text["Shared with 'it-audit-expiry-recipient' until ".Length..],
            "u", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var share = await LegacySharing.ShareOfAsync(vault, details);
        // The public text has second precision; the stored timestamp retains fractional seconds.
        Assert.InRange(share.ExpiresAtUtc!.Value - expiry, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromDays(7), share.ExpiresAtUtc - share.CreatedAtUtc);
    }

    [Fact]
    public async Task Revoking_from_secret_details_writes_one_audit_row_for_the_secret()
    {
        var issuer = await vault.AdminClientAsync();
        var recipient = await vault.SignInNewAccountAsync("it-audit-revoke-recipient", IPAddress.Parse("198.51.100.132"), project: "SHARE-AUDIT");
        var details = await LegacySharing.CreateSecretAsync(issuer, "it-audit-revoked-secret");
        Assert.Equal(HttpStatusCode.Redirect, (await LegacySharing.ShareAsync(issuer, details, "it-audit-revoke-recipient")).StatusCode);
        var share = await LegacySharing.ShareOfAsync(vault, details);
        Assert.Equal(HttpStatusCode.OK, (await recipient.Client.GetAsync(details)).StatusCode);
        var before = await LegacySharing.AuditHeadAsync(vault);

        var response = await LegacySharing.RevokeAsync(issuer, details, share.Id);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(details, response.Headers.Location?.OriginalString);
        var row = Assert.Single(await LegacySharing.ShareAuditRowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.SecretShareRevoked, row.Action);
        Assert.Equal("Secret", row.ResourceType);
        Assert.Equal(share.SecretId, row.ResourceId);
        Assert.Equal("it-audit-revoked-secret", row.ResourceName);
        Assert.Equal("Revoked access for 'it-audit-revoke-recipient'", row.Details);
        Assert.StartsWith("/Account/Denied?", (await recipient.Client.GetAsync(details)).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Issuer_scoped_revocation_records_the_secret_name_without_opening_its_details()
    {
        var issuer = await vault.SignInNewAccountAsync("it-audit-scoped-issuer", IPAddress.Parse("198.51.100.133"));
        await vault.SignInNewAccountAsync("it-audit-scoped-recipient", IPAddress.Parse("198.51.100.134"));
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-audit-scoped-secret");
        Assert.Equal(HttpStatusCode.Redirect, (await LegacySharing.ShareAsync(issuer.Client, details, "it-audit-scoped-recipient")).StatusCode);
        var share = await LegacySharing.ShareOfAsync(vault, details);
        var before = await LegacySharing.AuditHeadAsync(vault);

        var response = await LegacySharing.RevokeIssuedAsync(issuer.Client, share.Id);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Secrets/SharedByMe", response.Headers.Location?.OriginalString);
        var row = Assert.Single(await LegacySharing.ShareAuditRowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.SecretShareRevoked, row.Action);
        Assert.Equal("Secret", row.ResourceType);
        Assert.Equal(share.SecretId, row.ResourceId);
        Assert.Equal("it-audit-scoped-secret", row.ResourceName);
        Assert.Equal("Revoked access for 'it-audit-scoped-recipient'", row.Details);
        Assert.Equal("it-audit-scoped-issuer", row.Username);
    }
}
