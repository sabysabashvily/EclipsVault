using System.Net;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditFailureCollection.Name)]
public sealed class DataExportAuditFailureTests(AuditFailureFixture failure)
{
    [Fact]
    public async Task A_failed_audit_write_refuses_the_export_with_audit_unavailable_and_sends_no_file()
    {
        var vault = failure.Vault;
        var session = await vault.SignInNewAccountAsync("it-data-fail-export", IPAddress.Parse("198.51.100.209"));
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await UiApiRequest.WriteWithAntiforgeryAsync(session, HttpMethod.Post, "/api/ui/data/export");

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.Null(response.Content.Headers.ContentDisposition);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("it-data-fail-export", body);
            Assert.DoesNotContain("SchemaVersion", body);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }
}
