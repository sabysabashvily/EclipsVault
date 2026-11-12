using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditTamperingCollection.Name)]
public sealed class AuditTamperingTests(AuditTamperingFixture fixture)
{
    private readonly VaultFixture _vault = fixture.Vault;

    [Fact]
    public async Task Verifying_a_rewritten_entry_records_a_critical_row_naming_it()
    {
        var admin = await _vault.AdminClientAsync();
        var token = await LegacyAccessRequests.TokenAsync(admin);
        var rewritten = await AuditTrail.HeadAsync(_vault);
        var original = await DetailsOfAsync(rewritten);

        HttpResponseMessage response;
        await SetDetailsAsync(rewritten, "rewritten by someone with database access");
        try
        {
            response = await LegacyAuditPage.PostAsync(admin, "/Audit/Verify", token);
        }
        finally
        {
            await SetDetailsAsync(rewritten, original);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Tampering detected.", await response.Content.ReadAsStringAsync());
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(_vault, rewritten), row => row.Action == AuditAction.AuditChainVerified);
        Assert.True(row.IsCritical);
        Assert.Equal(
            $"Tampering detected. Entry #{rewritten} has been altered — its hash no longer matches its content. (First broken entry: #{rewritten}.)",
            row.Details);
    }

    private async Task<string?> DetailsOfAsync(long sequence)
    {
        using var scope = _vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>()
            .AuditLogs.Where(row => row.Sequence == sequence).Select(row => row.Details).SingleAsync();
    }

    private async Task SetDetailsAsync(long sequence, string? details)
    {
        using var scope = _vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>()
            .AuditLogs.Where(row => row.Sequence == sequence)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Details, details));
    }
}
