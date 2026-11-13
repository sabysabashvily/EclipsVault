using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditTamperingCollection.Name)]
public sealed class AuditExportStreamingTests(AuditTamperingFixture fixture)
{
    private readonly VaultFixture _vault = fixture.Vault;

    [Fact]
    public async Task The_entries_are_read_when_the_bundle_is_written_not_when_the_export_is_recorded()
    {
        const string rewritten = "Rewritten after the export was recorded";
        await using var scope = _vault.Services.CreateAsyncScope();
        await using var export = await scope.ServiceProvider.GetRequiredService<IAuditCheckpointService>().ExportAsync(default);

        using var written = new MemoryStream();
        await using (await TamperedTrail.RewriteFromAsync(_vault, export.Checkpoint.Sequence, rewritten))
        {
            await export.WriteToAsync(written, default);
        }

        var bundle = Assert.IsType<AuditBundle>(JsonSerializer.Deserialize<AuditBundle>(written.ToArray()));
        Assert.Equal(rewritten, bundle.Rows[^1].Details);
    }
}
