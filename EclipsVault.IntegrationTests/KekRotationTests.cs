using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.KeyManagement;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Master key rotation on a vault that loads a retired key. Overlaps are made deterministic by holding
/// the audit chain: every batch reads its rows before it waits for the chain, so a test can change
/// things between the read and the commit without timing anything.
/// </summary>
[Collection(KekRotationCollection.Name)]
public sealed class KekRotationTests(KekRotationFixture fixture)
{
    private readonly VaultFixture _vault = fixture.Vault;

    private string RetiredKekId => SecretsUnderMasterKey.IdOf(fixture.RetiredMasterKey);

    private string CurrentKekId => _vault.Services.GetRequiredService<IKekProvider>().CurrentKekId;

    [Fact]
    public async Task A_pass_over_more_than_one_batch_records_one_KekRotated_row_per_batch_and_no_SecretUpdated_row()
    {
        await RotateAsync();
        var secretIds = new List<Guid>();
        for (var index = 0; index < 501; index++)
        {
            secretIds.Add(await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, $"batch-{index:D3}", "batched value"));
        }
        await SecretsUnderMasterKey.ReplaceValueAsync(_vault, fixture.RetiredMasterKey, secretIds[0], "batched value, replaced");
        var before = await AuditTrail.HeadAsync(_vault);

        var result = await RotateAsync();

        Assert.Equal(new KekRotationResult(CurrentKekId, 501, 1, 0), result);
        var rows = await AuditTrail.RowsAfterAsync(_vault, before);
        var rotations = rows.Where(row => row.Action == AuditAction.KekRotated).ToArray();
        Assert.Equal(
            [
                $"Re-wrapped 500 secret(s) and 0 archived version(s) from KEK {RetiredKekId} to KEK {CurrentKekId}",
                $"Re-wrapped 1 secret(s) and 0 archived version(s) from KEK {RetiredKekId} to KEK {CurrentKekId}",
                $"Re-wrapped 0 secret(s) and 1 archived version(s) from KEK {RetiredKekId} to KEK {CurrentKekId}",
            ],
            rotations.Select(row => row.Details));
        Assert.All(rotations, row => Assert.True(row.IsCritical));
        Assert.DoesNotContain(rows, row => row.Action == AuditAction.SecretUpdated && secretIds.Contains(row.ResourceId!.Value));
    }

    [Fact]
    public async Task Secrets_and_versions_moved_by_a_pass_decrypt_under_the_current_key()
    {
        var secretId = await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "moved-secret", "first value");
        await SecretsUnderMasterKey.ReplaceValueAsync(_vault, fixture.RetiredMasterKey, secretId, "second value");

        await RotateAsync();

        Assert.Equal(CurrentKekId, await KekIdOfSecretAsync(secretId));
        Assert.Equal(CurrentKekId, await KekIdOfOnlyVersionAsync(secretId));
        Assert.Equal("second value", await RevealAsync(secretId));
        Assert.Equal("first value", await RevealOnlyVersionAsync(secretId));
    }

    [Fact]
    public async Task A_value_replaced_after_the_pass_read_it_keeps_its_new_value()
    {
        await RotateAsync();
        var secretId = await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "replaced-mid-pass", "value the pass read");

        var held = await HeldAuditChain.TakeAsync(_vault);
        Task<KekRotationResult> pass;
        try
        {
            pass = RotateAsync();
            await held.WaitForQueuedWritersAsync(1);
            await held.Services.GetRequiredService<ISecretService>().RotateAsync(secretId, "value written after the read", null, null, default);
            await held.CommitAsync();
        }
        finally
        {
            await held.DisposeAsync();
        }

        var result = await pass;
        Assert.Equal("value written after the read", await RevealAsync(secretId));
        Assert.Equal("value the pass read", await RevealOnlyVersionAsync(secretId));
        Assert.Equal(new KekRotationResult(CurrentKekId, 0, 0, 0), result);
    }

    [Fact]
    public async Task An_audited_write_queued_during_a_pass_commits_between_its_batches()
    {
        await RotateAsync();
        var secretId = await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "batched-around", "first value");
        await SecretsUnderMasterKey.ReplaceValueAsync(_vault, fixture.RetiredMasterKey, secretId, "second value");
        var before = await AuditTrail.HeadAsync(_vault);

        Task<KekRotationResult> pass;
        Task<Guid> write;
        await using (var held = await HeldAuditChain.TakeAsync(_vault))
        {
            pass = RotateAsync();
            await held.WaitForQueuedWritersAsync(1);
            write = CreateSecretAsync("written-between-batches");
            await held.WaitForQueuedWritersAsync(2);
        }

        await pass;
        var writtenId = await write;
        var rows = await AuditTrail.RowsAfterAsync(_vault, before);
        Assert.Equal(
            [AuditAction.KekRotated, AuditAction.SecretCreated, AuditAction.KekRotated],
            rows.Where(row => row.Action == AuditAction.KekRotated || row.ResourceId == writtenId).Select(row => row.Action));
    }

    [Fact]
    public async Task A_second_pass_while_one_runs_is_refused_and_changes_nothing()
    {
        await RotateAsync();
        await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "rotated-once", "value");
        var before = await AuditTrail.HeadAsync(_vault);

        Task<KekRotationResult> first;
        await using (var held = await HeldAuditChain.TakeAsync(_vault))
        {
            first = RotateAsync();
            await held.WaitForQueuedWritersAsync(1);

            await Assert.ThrowsAsync<KekRotationRunningException>(() => RotateAsync());
        }

        Assert.Equal(new KekRotationResult(CurrentKekId, 1, 0, 0), await first);
        Assert.Single(await AuditTrail.RowsAfterAsync(_vault, before), row => row.Action == AuditAction.KekRotated);
    }

    [Fact]
    public async Task A_row_under_a_key_that_is_not_loaded_is_skipped_and_counted_while_the_rest_move()
    {
        var missingMasterKey = SecretsUnderMasterKey.NewMasterKey();
        var stranded = await SecretsUnderMasterKey.CreateAsync(_vault, missingMasterKey, "under-missing-key", "value");
        try
        {
            var movable = await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "beside-missing-key", "value");

            var result = await RotateAsync();

            Assert.Equal(1, result.OnMissingKeys);
            Assert.Equal(CurrentKekId, await KekIdOfSecretAsync(movable));
            Assert.Equal(SecretsUnderMasterKey.IdOf(missingMasterKey), await KekIdOfSecretAsync(stranded));
        }
        finally
        {
            using var scope = _vault.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISecretService>().DeleteAsync(stranded, default);
        }
    }

    [Fact]
    public async Task A_pass_clears_the_secrets_it_moved_from_the_secret_cache()
    {
        var secretId = await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "cached-envelope", "value");
        using (var scope = _vault.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISecretService>().GetDetailsAsync(secretId, default);
        }
        var cache = _vault.Services.GetRequiredService<ISecretCache>();
        Assert.NotNull(await cache.GetAsync(secretId));

        await RotateAsync();

        Assert.Null(await cache.GetAsync(secretId));
    }

    [Fact]
    public async Task A_pass_moves_a_decoy_without_tripping_it()
    {
        var decoyId = await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "rotation-decoy", "bait");
        using (var scope = _vault.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets
                .Where(secret => secret.Id == decoyId)
                .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.IsHoneyToken, true));
        }
        var before = await AuditTrail.HeadAsync(_vault);

        await RotateAsync();

        Assert.Equal(CurrentKekId, await KekIdOfSecretAsync(decoyId));
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(_vault, before), row => row.Action == AuditAction.HoneyTokenTripped);
    }

    [Fact]
    public async Task The_Legacy_Rotate_action_runs_the_pass_and_shows_its_counts()
    {
        await RotateAsync();
        await SecretsUnderMasterKey.CreateAsync(_vault, fixture.RetiredMasterKey, "rotated-from-legacy", "value");
        var admin = await _vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(_vault);

        var token = VaultFixture.FormTokenOf(await admin.GetStringAsync("/Encryption"), "/Encryption");
        var response = await admin.PostAsync("/Encryption/Rotate",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"Rotated 1 secret(s) and 0 version(s) to KEK {CurrentKekId}.", await admin.GetStringAsync(response.Headers.Location));
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(_vault, before), row => row.Action == AuditAction.KekRotated);
        Assert.Equal(VaultFixture.AdminUsername, row.Username);
    }

    private async Task<KekRotationResult> RotateAsync()
    {
        using var scope = _vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IKekRotationService>().RotateAsync(default);
    }

    private async Task<Guid> CreateSecretAsync(string name)
    {
        using var scope = _vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISecretService>().CreateAsync(
            new CreateSecretRequest(name, "value", "ROTATION", SecretEnvironment.Development, SensitivityLevel.Internal, TtlDays: 0), default);
    }

    private async Task<string> RevealAsync(Guid secretId)
    {
        using var scope = _vault.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ISecretService>().RevealAsync(secretId, default)).Value;
    }

    private async Task<string> RevealOnlyVersionAsync(Guid secretId)
    {
        using var scope = _vault.Services.CreateScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
        var version = Assert.Single(await secrets.ListVersionsAsync(secretId, default));
        return (await secrets.RevealVersionAsync(secretId, version.Id, default)).Value;
    }

    private Task<string> KekIdOfSecretAsync(Guid secretId)
        => ReadAsync(db => db.Secrets.Where(secret => secret.Id == secretId).Select(secret => secret.KekId).SingleAsync());

    private Task<string> KekIdOfOnlyVersionAsync(Guid secretId)
        => ReadAsync(db => db.SecretVersions.Where(version => version.SecretId == secretId).Select(version => version.KekId).SingleAsync());

    private async Task<T> ReadAsync<T>(Func<EclipsVaultDbContext, Task<T>> read)
    {
        using var scope = _vault.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>());
    }
}
