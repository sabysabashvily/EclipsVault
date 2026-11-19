using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// One writer at a time per backend login, so one writer's put-back can never undo another's change. The
/// first upstream rotation has changed the real login and waits for the held audit chain when the second
/// writer starts, and the second is refused before it reaches the chain. Only the audit-failure vault has
/// a managed target, so these run there with auditing on.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class UpstreamRotationRaceTests(AuditFailureFixture failure)
{
    [Fact]
    public async Task A_second_upstream_rotation_of_one_login_is_refused_and_the_login_ends_at_the_stored_value()
    {
        var (vault, details) = await ManagedSecretAsync("it_race_two_upstream");
        var secretId = SecretSharing.SecretIdOf(details);

        Task first;
        await using (var held = await HeldAuditChain.TakeAsync(vault))
        {
            first = RotateUpstreamAsync(vault, secretId);
            await held.WaitForQueuedWritersAsync(1);
            await Assert.ThrowsAsync<ChangeInProgressException>(() => RotateUpstreamAsync(vault, secretId));
        }

        await first;
        Assert.True(await ManagedTargetLogins.OpensWithAsync(vault, "it_race_two_upstream", await UpstreamRotationTests.StoredValueAsync(vault, details)));
    }

    [Fact]
    public async Task A_rotation_by_hand_during_an_upstream_rotation_is_refused_and_the_login_ends_at_the_stored_value()
    {
        var (vault, details) = await ManagedSecretAsync("it_race_upstream_and_hand");
        var secretId = SecretSharing.SecretIdOf(details);

        Task upstream;
        await using (var held = await HeldAuditChain.TakeAsync(vault))
        {
            upstream = RotateUpstreamAsync(vault, secretId);
            await held.WaitForQueuedWritersAsync(1);
            await Assert.ThrowsAsync<ChangeInProgressException>(() => RotateByHandAsync(vault, secretId));
        }

        await upstream;
        Assert.True(await ManagedTargetLogins.OpensWithAsync(vault, "it_race_upstream_and_hand", await UpstreamRotationTests.StoredValueAsync(vault, details)));
    }

    /// <summary>
    /// A secret holding a real login's password and bound to it, rotated upstream once: the licence gate
    /// records its one-time rows on a vault's first rotation, which must not queue behind the held chain.
    /// </summary>
    private async Task<(VaultFixture Vault, string Details)> ManagedSecretAsync(string login)
    {
        var vault = failure.Vault;
        var password = ManagedTargetLogins.NewPassword();
        await ManagedTargetLogins.CreateAsync(vault, login, password);
        var details = await SecretSharing.CreateSecretAsync(await vault.AdminClientAsync(), login.Replace('_', '-'), value: password);
        await SecretDetailsApi.MakeManagedAsync(vault, details, login);
        await RotateUpstreamAsync(vault, SecretSharing.SecretIdOf(details));
        return (vault, details);
    }

    private static async Task RotateUpstreamAsync(VaultFixture vault, Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISecretService>().RotateManagedAsync(secretId, null, default);
    }

    private static async Task RotateByHandAsync(VaultFixture vault, Guid secretId)
    {
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISecretService>().RotateAsync(secretId, "pastedValue1", null, null, default);
    }
}
