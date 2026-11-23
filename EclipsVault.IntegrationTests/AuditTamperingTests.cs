using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);
        var rewritten = await AuditTrail.HeadAsync(_vault);
        var original = await DetailsOfAsync(rewritten);

        HttpResponseMessage response;
        await SetDetailsAsync(rewritten, "rewritten by someone with database access");
        try
        {
            response = await UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, "/api/ui/audit/verify");
        }
        finally
        {
            await SetDetailsAsync(rewritten, original);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(_vault, rewritten), row => row.Action == AuditAction.AuditChainVerified);
        await AssertTamperedAsync(response, row);
        Assert.True(row.IsCritical);
        Assert.Equal(
            $"Tampering detected. Entry #{rewritten} has been altered — its hash no longer matches its content. (First broken entry: #{rewritten}.)",
            row.Details);
    }

    [Fact]
    public async Task Deleting_the_newest_entries_back_past_a_checkpoint_is_reported_as_tampering()
    {
        var admin = await _vault.AdminClientAsync();
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);
        var checkpoint = await CreateCheckpointAsync(admin, token);

        HttpResponseMessage response;
        AuditLog row;
        await using (await TamperedTrail.DeleteFromAsync(_vault, checkpoint))
        {
            response = await UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, "/api/ui/audit/verify");
            row = Assert.Single(await AuditTrail.RowsAfterAsync(_vault, checkpoint - 1), row => row.Action == AuditAction.AuditChainVerified);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertTamperedAsync(response, row);
        Assert.True(row.IsCritical);
        Assert.Equal(
            $"Tampering detected. The chain ends at #{checkpoint - 1}, before the signed checkpoint at #{checkpoint}: entries were deleted. (First broken entry: #{checkpoint}.)",
            row.Details);
    }

    [Fact]
    public async Task Rewriting_a_signed_entry_and_recomputing_every_later_hash_is_reported_at_that_entry()
    {
        var admin = await _vault.AdminClientAsync();
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);
        var checkpoint = await CreateCheckpointAsync(admin, token);
        var head = await AuditTrail.HeadAsync(_vault);

        HttpResponseMessage response;
        AuditLog row;
        await using (await TamperedTrail.RewriteFromAsync(_vault, checkpoint, "rewritten by someone with database access"))
        {
            response = await UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, "/api/ui/audit/verify");
            row = Assert.Single(await AuditTrail.RowsAfterAsync(_vault, head), row => row.Action == AuditAction.AuditChainVerified);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertTamperedAsync(response, row);
        Assert.True(row.IsCritical);
        Assert.Equal(
            $"Tampering detected. Entry #{checkpoint} does not match the signed checkpoint: the trail was rewritten. (First broken entry: #{checkpoint}.)",
            row.Details);
    }

    /// <summary>Signs the trail through the UI API and returns the sequence the checkpoint covers.</summary>
    private static async Task<long> CreateCheckpointAsync(HttpClient admin, string token)
    {
        var response = await UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, "/api/ui/audit/checkpoints");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sequence").GetInt64();
    }

    /// <summary>The answer names the outcome and repeats the recorded reason, without the row's "Tampering detected." prefix.</summary>
    private static async Task AssertTamperedAsync(HttpResponseMessage response, AuditLog row)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tampered", body.GetProperty("outcome").GetString());
        Assert.Equal($"Tampering detected. {body.GetProperty("message").GetString()}", row.Details);
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
