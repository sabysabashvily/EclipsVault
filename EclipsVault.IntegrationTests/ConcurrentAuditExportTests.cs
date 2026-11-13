using System.Text.Json;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditTamperingCollection.Name)]
public sealed class ConcurrentAuditExportTests(AuditTamperingFixture fixture)
{
    private readonly VaultFixture _vault = fixture.Vault;

    [Fact]
    public async Task A_second_export_while_one_runs_is_refused_as_busy()
    {
        Task<AuditBundle> first;
        await using (var held = await HeldAuditChain.TakeAsync(_vault))
        {
            first = ExportAsync();
            // The first has signed the head and now waits on the chain to record itself.
            await held.WaitForQueuedWritersAsync(1);

            await Assert.ThrowsAsync<AuditBusyException>(ExportAsync);
        }

        Assert.True(AuditBundleVerifier.Verify(await first).IsValid);
        Assert.True(AuditBundleVerifier.Verify(await ExportAsync()).IsValid);
    }

    private async Task<AuditBundle> ExportAsync()
    {
        await using var scope = _vault.Services.CreateAsyncScope();
        await using var export = await scope.ServiceProvider.GetRequiredService<IAuditCheckpointService>().ExportAsync(default);
        using var written = new MemoryStream();
        await export.WriteToAsync(written, default);
        return Assert.IsType<AuditBundle>(JsonSerializer.Deserialize<AuditBundle>(written.ToArray()));
    }
}
