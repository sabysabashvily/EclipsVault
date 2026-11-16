using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Managed rotation against a real SQL Server login: the vault changes the login's password and stores
/// the same one. Only the audit-failure vault has a managed target, so these run there with auditing on.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class UpstreamRotationTests(AuditFailureFixture failure)
{
    [Fact]
    public async Task Rotating_upstream_moves_the_login_and_the_stored_value_together_and_records_only_the_rotation()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var password = ManagedTargetLogins.NewPassword();
        await ManagedTargetLogins.CreateAsync(vault, "it_upstream_rotated", password);
        var details = await LegacySharing.CreateSecretAsync(admin, "it-upstream-rotated", value: password);
        await LegacySecretActions.MakeManagedAsync(vault, details, "it_upstream_rotated");
        var token = await LegacySecretActions.FormTokenAsync(admin, details);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacySecretActions.RotateUpstreamAsync(admin, details, token);

        LegacySecretActions.AssertRedirectedTo(response, details);
        Assert.Equal([AuditAction.SecretUpstreamRotated], await LegacySecretActions.ActionsAfterAsync(vault, before, details));
        var stored = await StoredValueAsync(vault, details);
        Assert.NotEqual(password, stored);
        Assert.True(await ManagedTargetLogins.OpensWithAsync(vault, "it_upstream_rotated", stored));
        Assert.False(await ManagedTargetLogins.OpensWithAsync(vault, "it_upstream_rotated", password));
    }

    [Fact]
    public async Task A_login_the_server_refuses_to_change_is_reported_unchanged_and_records_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-upstream-refused", value: "it-upstream-refused-value");
        await LegacySecretActions.MakeManagedAsync(vault, details, "it_upstream_no_such_login");
        var token = await LegacySecretActions.FormTokenAsync(admin, details);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacySecretActions.RotateUpstreamAsync(admin, details, token);

        LegacySecretActions.AssertRedirectedTo(response, details);
        Assert.Empty(await LegacySecretActions.ActionsAfterAsync(vault, before, details));
        Assert.Contains("The backend refused the rotation. The credential is unchanged.", await admin.GetStringAsync(details));
        Assert.Equal("it-upstream-refused-value", await StoredValueAsync(vault, details));
    }

    /// <summary>Read through the vault's own service, which records the reveal after the test's assertions on the trail.</summary>
    internal static async Task<string> StoredValueAsync(VaultFixture vault, string details)
    {
        using var scope = vault.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ISecretService>().RevealAsync(LegacySharing.SecretIdOf(details), default)).Value;
    }
}
