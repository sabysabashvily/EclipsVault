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
/// writes are rejected once some of its batches have committed, so a later batch is the step that fails.
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
        var held = await HeldAuditChain.TakeAsync(vault);
        try
        {
            pass = RotateAsync(vault);
            held = await held.HandOverAfterNextWriterAsync(vault);
            await failure.RejectAuditWritesAsync();
        }
        finally
        {
            await held.DisposeAsync();
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
    public async Task Rotating_over_the_UI_API_answers_audit_unavailable_with_everything_the_committed_batches_moved()
    {
        var vault = failure.Vault;
        var currentKekId = vault.Services.GetRequiredService<IKekProvider>().CurrentKekId;
        var retiredKekId = SecretsUnderMasterKey.IdOf(failure.RetiredMasterKey);
        await RotateAsync(vault);
        var secretIds = await SecretsUnderMasterKey.CreateManyAsync(vault, failure.RetiredMasterKey, "stopped-over-http", count: 501);
        await SecretsUnderMasterKey.ReplaceEachValueAsync(vault, failure.RetiredMasterKey, secretIds, "second value");
        var admin = await vault.AdminClientAsync();
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);
        var before = await AuditTrail.HeadAsync(vault);

        // Three batches commit before audit writes are rejected: 500 secrets, 1 secret, then 500 versions.
        Task<HttpResponseMessage> pass;
        var held = await HeldAuditChain.TakeAsync(vault);
        try
        {
            pass = UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, "/api/ui/encryption/rotate");
            held = await held.HandOverAfterNextWriterAsync(vault);
            held = await held.HandOverAfterNextWriterAsync(vault);
            held = await held.HandOverAfterNextWriterAsync(vault);
            await failure.RejectAuditWritesAsync();
        }
        finally
        {
            await held.DisposeAsync();
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
        Assert.Equal(501, problem.GetProperty("secretsRewrapped").GetInt32());
        Assert.Equal(500, problem.GetProperty("versionsRewrapped").GetInt32());
        Assert.Equal(
            "Rotation stopped because the audit trail could not record it. 1001 item(s) were re-wrapped before it stopped; run it again to continue.",
            problem.GetProperty("title").GetString());

        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        Assert.Equal(501, await db.Secrets.CountAsync(secret => secretIds.Contains(secret.Id) && secret.KekId == currentKekId));
        Assert.Equal(500, await db.SecretVersions.CountAsync(version => secretIds.Contains(version.SecretId) && version.KekId == currentKekId));
        Assert.Equal(1, await db.SecretVersions.CountAsync(version => secretIds.Contains(version.SecretId) && version.KekId == retiredKekId));
        Assert.Equal(
            [
                $"Re-wrapped 500 secret(s) and 0 archived version(s) from KEK {retiredKekId} to KEK {currentKekId}",
                $"Re-wrapped 1 secret(s) and 0 archived version(s) from KEK {retiredKekId} to KEK {currentKekId}",
                $"Re-wrapped 0 secret(s) and 500 archived version(s) from KEK {retiredKekId} to KEK {currentKekId}",
            ],
            (await AuditTrail.RowsAfterAsync(vault, before)).Where(row => row.Action == AuditAction.KekRotated).Select(row => row.Details));
    }

    private static async Task<KekRotationResult> RotateAsync(VaultFixture vault)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IKekRotationService>().RotateAsync(default);
    }
}
