using System.Net;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditTamperingCollection.Name)]
public sealed class ConcurrentAuditExportTests(AuditTamperingFixture fixture)
{
    private readonly VaultFixture _vault = fixture.Vault;

    [Fact]
    public async Task A_second_HTTP_export_while_one_runs_is_refused_without_a_bundle()
    {
        var firstSession = await _vault.SignInNewAccountAsync("it-export-first", IPAddress.Parse("203.0.113.167"), ClearanceLevel.TopSecret);
        var secondSession = await _vault.SignInNewAccountAsync("it-export-second", IPAddress.Parse("203.0.113.168"), ClearanceLevel.TopSecret);
        var firstToken = await LegacyAccessRequests.TokenAsync(firstSession.Client);
        var secondToken = await LegacyAccessRequests.TokenAsync(secondSession.Client);

        Task<HttpResponseMessage> first;
        await using (var held = await HeldAuditChain.TakeAsync(_vault))
        {
            first = LegacyAuditPage.PostAsync(firstSession.Client, "/Audit/Export", firstToken);
            // The first has signed the head and now waits on the chain to record itself.
            await held.WaitForQueuedWritersAsync(1);

            using var second = await LegacyAuditPage.PostAsync(secondSession.Client, "/Audit/Export", secondToken);

            Assert.Equal(HttpStatusCode.Found, second.StatusCode);
            Assert.Equal("/Home/Error?code=400", second.Headers.Location?.OriginalString);
            Assert.Null(second.Content.Headers.ContentDisposition);
            Assert.Empty(await second.Content.ReadAsByteArrayAsync());
        }

        using var completed = await first;

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var bundle = Assert.IsType<AuditBundle>(JsonSerializer.Deserialize<AuditBundle>(await completed.Content.ReadAsStringAsync()));
        var publicKey = _vault.Services.GetRequiredService<IAuditCheckpointSigner>().PublicKeySpki;
        var verification = AuditBundleVerifier.Verify(bundle, publicKey);
        Assert.True(verification.IsValid, verification.Message);
    }
}
