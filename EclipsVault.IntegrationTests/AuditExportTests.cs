using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Exporting the audit trail through the Legacy page. Each test exports as an administrator of its own,
/// so it asserts only on the rows that administrator wrote.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class AuditExportTests(VaultFixture vault)
{
    [Fact]
    public async Task The_exported_bundle_verifies_against_the_vaults_own_public_key()
    {
        var session = await vault.SignInNewAccountAsync("it-audit-exporter", IPAddress.Parse("203.0.113.163"), ClearanceLevel.TopSecret);
        var token = await LegacyAccessRequests.TokenAsync(session.Client);

        var response = await LegacyAuditPage.PostAsync(session.Client, "/Audit/Export", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The offline verifier reads the file exactly this way.
        var bundle = Assert.IsType<AuditBundle>(JsonSerializer.Deserialize<AuditBundle>(await response.Content.ReadAsStringAsync()));
        var publicKey = vault.Services.GetRequiredService<IAuditCheckpointSigner>().PublicKeySpki;
        var verification = AuditBundleVerifier.Verify(bundle, publicKey);
        Assert.True(verification.IsValid, verification.Message);
    }

    [Fact]
    public async Task The_bundle_downloads_uncached_with_its_file_name_indentation_and_format()
    {
        var session = await vault.SignInNewAccountAsync("it-audit-export-file", IPAddress.Parse("203.0.113.164"), ClearanceLevel.TopSecret);
        var token = await LegacyAccessRequests.TokenAsync(session.Client);

        var response = await LegacyAuditPage.PostAsync(session.Client, "/Audit/Export", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Matches(@"^eclipsvault-audit-bundle-\d{8}-\d{6}\.json$", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Contains("\n  \"SchemaVersion\": \"eclipsvault.audit-bundle/2\",", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_bundle_is_signed_at_its_last_entry_and_its_export_is_recorded_after_it()
    {
        const string exporter = "it-audit-export-head";
        var session = await vault.SignInNewAccountAsync(exporter, IPAddress.Parse("203.0.113.165"), ClearanceLevel.TopSecret);
        var token = await LegacyAccessRequests.TokenAsync(session.Client);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAuditPage.PostAsync(session.Client, "/Audit/Export", token);

        var bundle = Assert.IsType<AuditBundle>(JsonSerializer.Deserialize<AuditBundle>(await response.Content.ReadAsStringAsync()));
        var last = bundle.Rows[^1];
        Assert.Equal((last.Sequence, last.EntryHash), (bundle.Checkpoint.Sequence, bundle.Checkpoint.ChainHeadHash));
        var recorded = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.Username == exporter);
        Assert.Equal(AuditAction.AuditBundleExported, recorded.Action);
        Assert.True(recorded.Sequence > last.Sequence);
        Assert.EndsWith($" at sequence {last.Sequence}", recorded.Details);
    }

    [Fact]
    public async Task A_stale_administrator_exports_only_after_proving_it_is_them()
    {
        const string exporter = "it-audit-stale-exporter";
        var administrator = await vault.SignInNewAccountAsync(exporter, IPAddress.Parse("203.0.113.166"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, administrator, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("203.0.113.166"));
        var token = await LegacyAccessRequests.TokenAsync(stale.Client);
        var before = await AuditTrail.HeadAsync(vault);

        await AdministratorFreshnessTests.AssertStepUpAsync(await LegacyAuditPage.PostAsync(stale.Client, "/Audit/Export", token), "code");
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Username == exporter && row.Action == AuditAction.AuditBundleExported);

        var owner = await ProfileHttp.AccountAsync(vault, exporter);
        var proof = await UiApiRequest.WriteWithAntiforgeryAsync(stale, HttpMethod.Post, "/api/ui/step-up", JsonContent.Create(new { code = Totp.Code(owner.TotpSecret!, Totp.CurrentStep() + 1) }));

        Assert.Equal(HttpStatusCode.NoContent, proof.StatusCode);
        var fresh = await LegacyAuditPage.PostAsync(stale.Client, "/Audit/Export", token);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.Equal("attachment", fresh.Content.Headers.ContentDisposition?.DispositionType);
    }
}
