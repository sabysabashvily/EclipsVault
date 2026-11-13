using System.Net;
using EclipsVault.Core.Domain.Entities;
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
        Assert.Contains("✗ Tampering detected.", await response.Content.ReadAsStringAsync());
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(_vault, rewritten), row => row.Action == AuditAction.AuditChainVerified);
        Assert.True(row.IsCritical);
        Assert.Equal(
            $"Tampering detected. Entry #{rewritten} has been altered — its hash no longer matches its content. (First broken entry: #{rewritten}.)",
            row.Details);
    }

    [Fact]
    public async Task Deleting_the_newest_entries_back_past_a_checkpoint_is_reported_as_tampering()
    {
        var admin = await _vault.AdminClientAsync();
        var token = await LegacyAccessRequests.TokenAsync(admin);
        var checkpoint = await CreateCheckpointAsync(admin, token);

        HttpResponseMessage response;
        AuditLog row;
        await using (await TamperedTrail.DeleteFromAsync(_vault, checkpoint))
        {
            response = await LegacyAuditPage.PostAsync(admin, "/Audit/Verify", token);
            row = Assert.Single(await AuditTrail.RowsAfterAsync(_vault, checkpoint - 1), row => row.Action == AuditAction.AuditChainVerified);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("✗ Tampering detected.", await response.Content.ReadAsStringAsync());
        Assert.True(row.IsCritical);
        Assert.Equal(
            $"Tampering detected. The chain ends at #{checkpoint - 1}, before the signed checkpoint at #{checkpoint}: entries were deleted. (First broken entry: #{checkpoint}.)",
            row.Details);
    }

    [Fact]
    public async Task Rewriting_a_signed_entry_and_recomputing_every_later_hash_is_reported_at_that_entry()
    {
        var admin = await _vault.AdminClientAsync();
        var token = await LegacyAccessRequests.TokenAsync(admin);
        var checkpoint = await CreateCheckpointAsync(admin, token);
        var head = await AuditTrail.HeadAsync(_vault);

        HttpResponseMessage response;
        AuditLog row;
        await using (await TamperedTrail.RewriteFromAsync(_vault, checkpoint, "rewritten by someone with database access"))
        {
            response = await LegacyAuditPage.PostAsync(admin, "/Audit/Verify", token);
            row = Assert.Single(await AuditTrail.RowsAfterAsync(_vault, head), row => row.Action == AuditAction.AuditChainVerified);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("✗ Tampering detected.", await response.Content.ReadAsStringAsync());
        Assert.True(row.IsCritical);
        Assert.Equal(
            $"Tampering detected. Entry #{checkpoint} does not match the signed checkpoint: the trail was rewritten. (First broken entry: #{checkpoint}.)",
            row.Details);
    }

    /// <summary>Signs the trail through the Legacy page and returns the sequence the checkpoint covers.</summary>
    private async Task<long> CreateCheckpointAsync(HttpClient admin, string token)
    {
        Assert.Equal(HttpStatusCode.Redirect, (await LegacyAuditPage.PostAsync(admin, "/Audit/CreateCheckpoint", token)).StatusCode);
        using var scope = _vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AuditCheckpoints.MaxAsync(checkpoint => checkpoint.Sequence);
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
