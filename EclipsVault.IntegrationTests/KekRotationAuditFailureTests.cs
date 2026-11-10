using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.KeyManagement;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A rotation batch commits only with its audit row. The pass starts with auditing working; audit
/// writes are rejected after its first batch commits, so the second batch is the step that fails.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class KekRotationAuditFailureTests(AuditFailureFixture failure)
{
    [Fact]
    public async Task A_batch_that_cannot_be_recorded_rolls_back_and_earlier_batches_stay()
    {
        var vault = failure.Vault;
        var currentKekId = vault.Services.GetRequiredService<IKekProvider>().CurrentKekId;
        var retiredKekId = SecretsUnderMasterKey.IdOf(failure.RetiredMasterKey);
        var secretId = await SecretsUnderMasterKey.CreateAsync(vault, failure.RetiredMasterKey, "rotation-unrecorded", "first value");
        await SecretsUnderMasterKey.ReplaceValueAsync(vault, failure.RetiredMasterKey, secretId, "second value");
        var before = await AuditTrail.HeadAsync(vault);

        Task<KekRotationResult> pass;
        Task<HeldAuditChain> second;
        await using (var first = await HeldAuditChain.TakeAsync(vault))
        {
            pass = RotateAsync(vault);
            await first.WaitForQueuedWritersAsync(1);
            second = HeldAuditChain.TakeAsync(vault);
            await first.WaitForQueuedWritersAsync(2);
        }

        // The pass queued first, so the chain reaches the second holder only once the secrets batch has committed.
        await using (await second)
        {
            await failure.RejectAuditWritesAsync();
        }

        try
        {
            var stopped = await Assert.ThrowsAsync<KekRotationStoppedException>(() => pass);
            Assert.Equal((1, 0), (stopped.SecretsRewrapped, stopped.VersionsRewrapped));
            Assert.IsType<AuditWriteFailedException>(stopped.InnerException);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        Assert.Equal(currentKekId, await db.Secrets.Where(secret => secret.Id == secretId).Select(secret => secret.KekId).SingleAsync());
        Assert.Equal(retiredKekId, await db.SecretVersions.Where(version => version.SecretId == secretId).Select(version => version.KekId).SingleAsync());
        Assert.Equal(
            [$"Re-wrapped 1 secret(s) and 0 archived version(s) from KEK {retiredKekId} to KEK {currentKekId}"],
            (await AuditTrail.RowsAfterAsync(vault, before)).Where(row => row.Action == AuditAction.KekRotated).Select(row => row.Details));
    }

    [Fact]
    public async Task Rotating_over_the_UI_API_answers_audit_unavailable_with_what_moved_before_the_unrecorded_batch()
    {
        var vault = failure.Vault;
        await RotateAsync(vault);
        var secretId = await SecretsUnderMasterKey.CreateAsync(vault, failure.RetiredMasterKey, "rotation-stopped-over-http", "first value");
        await SecretsUnderMasterKey.ReplaceValueAsync(vault, failure.RetiredMasterKey, secretId, "second value");
        var admin = await vault.AdminClientAsync();
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);

        Task<HttpResponseMessage> pass;
        Task<HeldAuditChain> second;
        await using (var first = await HeldAuditChain.TakeAsync(vault))
        {
            pass = UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, "/api/ui/encryption/rotate");
            await first.WaitForQueuedWritersAsync(1);
            second = HeldAuditChain.TakeAsync(vault);
            await first.WaitForQueuedWritersAsync(2);
        }

        // The pass queued first, so the chain reaches the second holder only once the secrets batch has committed.
        await using (await second)
        {
            await failure.RejectAuditWritesAsync();
        }

        HttpResponseMessage response;
        try
        {
            response = await pass;
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["code", "secretsRewrapped", "status", "title", "versionsRewrapped"], ProfileApi.NamesOf(problem));
        Assert.Equal("audit_unavailable", problem.GetProperty("code").GetString());
        Assert.Equal(1, problem.GetProperty("secretsRewrapped").GetInt32());
        Assert.Equal(0, problem.GetProperty("versionsRewrapped").GetInt32());
        Assert.Equal(
            "Rotation stopped because the audit trail could not record it. 1 item(s) were re-wrapped before it stopped; run it again to continue.",
            problem.GetProperty("title").GetString());
    }

    private static async Task<KekRotationResult> RotateAsync(VaultFixture vault)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IKekRotationService>().RotateAsync(default);
    }
}
