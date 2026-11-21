using System.Net;
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
        var details = await SecretSharing.CreateSecretAsync(admin, "it-upstream-rotated", value: password);
        await SecretDetailsApi.MakeManagedAsync(vault, details, "it_upstream_rotated");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretDetailsApi.RotateUpstreamAsync(admin, details);

        SecretDetailsApi.AssertNoContent(response);
        Assert.Equal([AuditAction.SecretUpstreamRotated], await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
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
        var details = await SecretSharing.CreateSecretAsync(admin, "it-upstream-refused", value: "it-upstream-refused-value");
        await SecretDetailsApi.MakeManagedAsync(vault, details, "it_upstream_no_such_login");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretDetailsApi.RotateUpstreamAsync(admin, details);

        var problem = await SecretsApi.AssertProblemAsync(response, 502, "upstream_refused");
        Assert.Equal("The backend refused the rotation. The credential is unchanged.", problem.GetProperty("title").GetString());
        Assert.Empty(await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
        Assert.Equal("it-upstream-refused-value", await StoredValueAsync(vault, details));
    }

    [Fact]
    public async Task Restoring_a_version_of_a_managed_secret_is_refused_and_changes_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        await ManagedTargetLogins.CreateAsync(vault, "it_upstream_restore", ManagedTargetLogins.NewPassword());
        var details = await SecretSharing.CreateSecretAsync(admin, "it-upstream-restore", value: "it-upstream-restore-unused");
        await SecretDetailsApi.MakeManagedAsync(vault, details, "it_upstream_restore");
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateUpstreamAsync(admin, details));
        var live = await StoredValueAsync(vault, details);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretDetailsApi.RestoreAsync(admin, details, await SecretDetailsApi.NewestVersionAsync(vault, details));

        var problem = await SecretsApi.AssertProblemAsync(response, 409, "restore_managed");
        Assert.Equal(
            "This secret is the live password of 'it_upstream_restore'. Rotate it upstream instead; earlier versions stay readable.",
            problem.GetProperty("title").GetString());
        Assert.Empty(await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
        Assert.Equal(live, await StoredValueAsync(vault, details));
        Assert.True(await ManagedTargetLogins.OpensWithAsync(vault, "it_upstream_restore", live));
    }

    [Fact]
    public async Task A_project_member_rotates_a_managed_secret_upstream()
    {
        var vault = failure.Vault;
        var password = ManagedTargetLogins.NewPassword();
        await ManagedTargetLogins.CreateAsync(vault, "it_upstream_member", password);
        var details = await SecretSharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-upstream-member", value: password);
        await SecretDetailsApi.MakeManagedAsync(vault, details, "it_upstream_member");
        var member = await vault.SignInNewAccountAsync("it-upstream-member", IPAddress.Parse("10.167.0.10"));

        var response = await SecretDetailsApi.RotateUpstreamAsync(member.Client, details);

        SecretDetailsApi.AssertNoContent(response);
        var stored = await StoredValueAsync(vault, details);
        Assert.NotEqual(password, stored);
        Assert.True(await ManagedTargetLogins.OpensWithAsync(vault, "it_upstream_member", stored));
    }

    /// <summary>Read through the vault's own service, which records the reveal after the test's assertions on the trail.</summary>
    internal static async Task<string> StoredValueAsync(VaultFixture vault, string details)
    {
        using var scope = vault.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ISecretService>().RevealAsync(SecretSharing.SecretIdOf(details), default)).Value;
    }
}
