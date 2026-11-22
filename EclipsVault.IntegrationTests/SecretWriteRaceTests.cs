using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Two writes to one secret never overwrite each other, and shredding never writes over a newer value.
/// The overlap is made deterministic by holding the audit chain: every write reads the secret before it
/// waits for the chain. A write run through the holder's own services commits first; one started
/// earlier commits after it. Two writes queued behind the holder commit in the order they queued.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SecretWriteRaceTests(VaultFixture vault)
{
    [Fact]
    public async Task Of_two_rotations_the_one_that_read_first_and_committed_second_is_told_the_secret_changed()
    {
        var secretId = await CreateAsync("it-race-two-rotations", ttlDays: 0);

        Task loser;
        await using (var held = await HeldAuditChain.TakeAsync(vault))
        {
            loser = RotateAsync(secretId, "loser value", renewTtlDays: null);
            await held.WaitForQueuedWritersAsync(1);
            await held.Services.GetRequiredService<ISecretService>().RotateAsync(secretId, "winner value", null, null, default);
            await held.CommitAsync();
        }

        await Assert.ThrowsAsync<SecretChangedException>(() => loser);
        Assert.Equal("winner value", await RevealAsync(secretId));
        Assert.Equal(["first value"], await HistoryAsync(secretId));
    }

    [Fact]
    public async Task A_restore_that_read_before_a_rotation_committed_is_told_the_secret_changed()
    {
        var secretId = await CreateAsync("it-race-restore", ttlDays: 0);
        await RotateAsync(secretId, "second value", renewTtlDays: null);
        var versionId = await OnlyVersionIdAsync(secretId);

        Task loser;
        await using (var held = await HeldAuditChain.TakeAsync(vault))
        {
            loser = RestoreAsync(secretId, versionId);
            await held.WaitForQueuedWritersAsync(1);
            await held.Services.GetRequiredService<ISecretService>().RotateAsync(secretId, "winner value", null, null, default);
            await held.CommitAsync();
        }

        await Assert.ThrowsAsync<SecretChangedException>(() => loser);
        Assert.Equal("winner value", await RevealAsync(secretId));
        Assert.Equal(["second value", "first value"], await HistoryAsync(secretId));
    }

    [Fact]
    public async Task A_rotation_that_read_before_a_shred_committed_never_writes_onto_the_shredded_row()
    {
        var secretId = await CreateAsync("it-race-shred-before-rotation", ttlDays: 1);

        Task loser;
        await using (var held = await HeldAuditChain.TakeAsync(vault))
        {
            loser = RotateAsync(secretId, "loser value", renewTtlDays: 30);
            await held.WaitForQueuedWritersAsync(1);
            await ExpireAsync(secretId);
            Assert.True(await held.Services.GetRequiredService<ISecretService>().ShredIfExpiredAsync(secretId, default));
            await held.CommitAsync();
        }

        await Assert.ThrowsAsync<SecretChangedException>(() => loser);
        Assert.True(await IsShreddedAsync(secretId));
    }

    [Fact]
    public async Task A_restore_that_read_before_a_shred_committed_never_writes_onto_the_shredded_row()
    {
        var secretId = await CreateAsync("it-race-shred-before-restore", ttlDays: 1);
        await RotateAsync(secretId, "second value", renewTtlDays: null);
        var versionId = await OnlyVersionIdAsync(secretId);

        Task loser;
        await using (var held = await HeldAuditChain.TakeAsync(vault))
        {
            loser = RestoreAsync(secretId, versionId);
            await held.WaitForQueuedWritersAsync(1);
            await ExpireAsync(secretId);
            Assert.True(await held.Services.GetRequiredService<ISecretService>().ShredIfExpiredAsync(secretId, default));
            await held.CommitAsync();
        }

        await Assert.ThrowsAsync<SecretChangedException>(() => loser);
        Assert.True(await IsShreddedAsync(secretId));
    }

    [Fact]
    public async Task A_shred_that_read_before_a_renewing_rotation_committed_leaves_the_renewed_secret_alone()
    {
        var secretId = await CreateAsync("it-race-rotation-before-shred", ttlDays: 1);

        Task rotation;
        Task<bool> shred;
        await using (var held = await HeldAuditChain.TakeAsync(vault))
        {
            rotation = RotateAsync(secretId, "renewed value", renewTtlDays: 30);
            await held.WaitForQueuedWritersAsync(1);
            await ExpireAsync(secretId);
            shred = ShredIfExpiredAsync(secretId);
            await held.WaitForQueuedWritersAsync(2);
        }

        await rotation;
        Assert.False(await shred);
        Assert.False(await IsShreddedAsync(secretId));
        Assert.Equal("renewed value", await RevealAsync(secretId));
        Assert.Equal(["first value"], await HistoryAsync(secretId));
    }

    [Fact]
    public async Task A_shred_that_read_before_a_restore_committed_leaves_the_restored_secret_alone()
    {
        var secretId = await CreateAsync("it-race-restore-before-shred", ttlDays: 1);
        await RotateAsync(secretId, "second value", renewTtlDays: null);
        var versionId = await OnlyVersionIdAsync(secretId);

        Task restore;
        Task<bool> shred;
        await using (var held = await HeldAuditChain.TakeAsync(vault))
        {
            restore = RestoreAsync(secretId, versionId);
            await held.WaitForQueuedWritersAsync(1);
            await ExpireAsync(secretId);
            shred = ShredIfExpiredAsync(secretId);
            await held.WaitForQueuedWritersAsync(2);
        }

        await restore;
        Assert.False(await shred);
        Assert.False(await IsShreddedAsync(secretId));
        Assert.Equal(["second value", "first value"], await HistoryAsync(secretId));
    }

    private async Task<Guid> CreateAsync(string name, int ttlDays)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISecretService>().CreateAsync(
            new CreateSecretRequest(name, "first value", "RACES", SecretEnvironment.Development, SensitivityLevel.Internal, ttlDays), default);
    }

    private async Task RotateAsync(Guid secretId, string value, int? renewTtlDays)
    {
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISecretService>().RotateAsync(secretId, value, null, renewTtlDays, default);
    }

    private async Task RestoreAsync(Guid secretId, Guid versionId)
    {
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISecretService>().RestoreVersionAsync(secretId, versionId, default);
    }

    private async Task<bool> ShredIfExpiredAsync(Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISecretService>().ShredIfExpiredAsync(secretId, default);
    }

    private async Task<string> RevealAsync(Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ISecretService>().RevealAsync(secretId, default)).Value;
    }

    /// <summary>Every archived value, newest first, each decrypted.</summary>
    private async Task<string[]> HistoryAsync(Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
        var values = new List<string>();
        foreach (var version in await secrets.ListVersionsAsync(secretId, default))
        {
            values.Add((await secrets.RevealVersionAsync(secretId, version.Id, default)).Value);
        }

        return [.. values];
    }

    private async Task<Guid> OnlyVersionIdAsync(Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        return Assert.Single(await scope.ServiceProvider.GetRequiredService<ISecretService>().ListVersionsAsync(secretId, default)).Id;
    }

    /// <summary>Moves the expiry into the past directly: no page can make time pass.</summary>
    private async Task ExpireAsync(Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.Where(secret => secret.Id == secretId)
            .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    private async Task<bool> IsShreddedAsync(Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.AsNoTracking()
            .Where(secret => secret.Id == secretId).Select(secret => secret.IsShredded).SingleAsync();
    }
}
