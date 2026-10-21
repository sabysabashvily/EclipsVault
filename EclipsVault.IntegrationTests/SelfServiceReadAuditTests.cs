using System.Net;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class SelfServiceReadAuditTests(VaultFixture vault)
{
    [Theory]
    [InlineData("/api/ui/sessions")]
    [InlineData("/api/ui/data")]
    [InlineData("/api/ui/shared-by-me")]
    public async Task Reading_self_service_page_data_writes_no_audit_row(string path)
    {
        var client = await vault.AdminClientAsync();
        var before = await LegacySharing.AuditHeadAsync(vault);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }
}
