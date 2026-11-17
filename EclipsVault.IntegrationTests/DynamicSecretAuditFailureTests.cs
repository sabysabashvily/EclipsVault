using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Dynamic credentials when the audit trail rejects writes: nothing is issued unrecorded, and a
/// destroyed credential whose close cannot be recorded is closed by a later sweep.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class DynamicSecretAuditFailureTests(AuditFailureFixture failure)
{
    private readonly VaultFixture vault = failure.Vault;

    [Fact]
    public async Task An_issue_whose_lease_cannot_be_recorded_removes_the_login_and_stores_nothing()
    {
        var roleId = await LegacyDynamicSecrets.AddRoleAsync(vault, "it_dyn_af_issue");
        var admin = await vault.AdminClientAsync();
        var token = await LegacyDynamicSecrets.TokenAsync(admin);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyDynamicSecrets.IssueAsync(admin, roleId, token: token);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Home/Error?code=503", response.Headers.Location?.OriginalString);
            Assert.Empty(await LegacyDynamicSecrets.LoginsOfRoleAsync(vault, "it_dyn_af_issue"));
            Assert.Empty(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_destroyed_credential_whose_close_cannot_be_recorded_stays_active_until_a_recorded_sweep_expires_it()
    {
        var roleId = await LegacyDynamicSecrets.AddRoleAsync(vault, "it_dyn_af_revoke");
        var admin = await vault.AdminClientAsync();
        await LegacyDynamicSecrets.IssueAsync(admin, roleId);
        var lease = Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId));
        var token = await LegacyDynamicSecrets.TokenAsync(admin);

        await failure.RejectAuditWritesAsync();
        try
        {
            await LegacyDynamicSecrets.RevokeAsync(admin, lease.Id, token);

            Assert.Equal(
                "The credential was destroyed, but the vault couldn't record it in the audit trail. Its lease shows Active until it expires.",
                await LegacyDynamicSecrets.FlashAsync(admin));
            Assert.Empty(await LegacyDynamicSecrets.LoginsOfRoleAsync(vault, "it_dyn_af_revoke"));
            Assert.Equal(LeaseStatus.Active, Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId)).Status);

            await LegacyDynamicSecrets.ExpireAtAsync(vault, lease.Id, DateTimeOffset.UtcNow.AddSeconds(-1));
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => LegacyDynamicSecrets.SweepAsync(vault));

            Assert.Equal(LeaseStatus.Active, Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId)).Status);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await LegacyDynamicSecrets.SweepAsync(vault);

        Assert.Equal(LeaseStatus.Expired, Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId)).Status);
    }

    [Fact]
    public async Task A_sweep_whose_retry_of_a_one_shot_revocation_fails_closes_the_lease_as_revocation_failed()
    {
        var roleId = await LegacyDynamicSecrets.AddRoleAsync(vault, "it_dyn_af_one_shot", revocation: LegacyDynamicSecrets.OneShotRevocation);
        var admin = await vault.AdminClientAsync();
        await LegacyDynamicSecrets.IssueAsync(admin, roleId);
        var lease = Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId));
        var token = await LegacyDynamicSecrets.TokenAsync(admin);

        await failure.RejectAuditWritesAsync();
        try
        {
            await LegacyDynamicSecrets.RevokeAsync(admin, lease.Id, token);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
        await LegacyDynamicSecrets.ExpireAtAsync(vault, lease.Id, DateTimeOffset.UtcNow.AddSeconds(-1));

        await LegacyDynamicSecrets.SweepAsync(vault);

        var closed = Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId));
        Assert.Equal(LeaseStatus.RevocationFailed, closed.Status);
        Assert.Equal("The backend refused to destroy the credential; it may still work.", closed.RevocationError);
        Assert.Empty(await LegacyDynamicSecrets.LoginsOfRoleAsync(vault, "it_dyn_af_one_shot"));
    }
}
