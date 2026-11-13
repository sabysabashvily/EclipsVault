using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditTamperingCollection.Name)]
public sealed class ConcurrentAuditExportTests(AuditTamperingFixture fixture)
{
    private const string ExportPath = "/api/ui/audit/export";

    private readonly VaultFixture _vault = fixture.Vault;

    [Fact]
    public async Task A_second_HTTP_export_while_one_runs_is_refused_without_a_bundle()
    {
        var firstSession = await _vault.SignInNewAccountAsync("it-export-first", IPAddress.Parse("203.0.113.167"), ClearanceLevel.TopSecret);
        var secondSession = await _vault.SignInNewAccountAsync("it-export-second", IPAddress.Parse("203.0.113.168"), ClearanceLevel.TopSecret);
        var firstToken = await UiApiRequest.AntiforgeryTokenAsync(firstSession.Client);
        var secondToken = await UiApiRequest.AntiforgeryTokenAsync(secondSession.Client);

        // The first use writes a licensing reminder before it takes the export lock.
        await _vault.Services.GetRequiredService<IPremiumFeatureUsage>().RecordUseAsync(LicenseFeatures.AuditAttestation, default);

        Task<HttpResponseMessage> first;
        await using (var held = await HeldAuditChain.TakeAsync(_vault))
        {
            first = UiApiRequest.SendWithTokenAsync(firstSession.Client, firstToken, HttpMethod.Post, ExportPath);
            // The first has signed the head and now waits on the chain to record itself.
            await held.WaitForQueuedWritersAsync(1);

            using var second = await UiApiRequest.SendWithTokenAsync(secondSession.Client, secondToken, HttpMethod.Post, ExportPath);

            await AssertBusyAsync(second);
            Assert.Null(second.Content.Headers.ContentDisposition);
        }

        using var completed = await first;

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var bundle = Assert.IsType<AuditBundle>(JsonSerializer.Deserialize<AuditBundle>(await completed.Content.ReadAsStringAsync()));
        var publicKey = _vault.Services.GetRequiredService<IAuditCheckpointSigner>().PublicKeySpki;
        var verification = AuditBundleVerifier.Verify(bundle, publicKey);
        Assert.True(verification.IsValid, verification.Message);
    }

    internal static async Task AssertBusyAsync(HttpResponseMessage response)
    {
        await UiApiFailureContractTests.AssertProblemAsync(response, 409, "audit_busy");
        Assert.Equal("Another verification or export is running. Try again when it finishes.",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }
}
