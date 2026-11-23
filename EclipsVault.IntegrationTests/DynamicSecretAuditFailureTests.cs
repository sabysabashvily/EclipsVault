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
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_af_issue");
        var admin = await vault.AdminClientAsync();
        var token = await DynamicSecretsApi.TokenAsync(admin);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await DynamicSecretsApi.IssueAsync(admin, roleId, token: token);

            await DynamicSecretsApi.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.Empty(await DynamicSecretsApi.LoginsOfRoleAsync(vault, "it_dyn_af_issue"));
            Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_postgres_issue_whose_lease_cannot_be_recorded_removes_the_role_and_stores_nothing()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_af_issue");
        var admin = await vault.AdminClientAsync();
        var token = await DynamicSecretsApi.TokenAsync(admin);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await DynamicSecretsApi.IssueAsync(admin, roleId, token: token);

            await DynamicSecretsApi.AssertProblemAsync(response, 503, "audit_unavailable");
            // The role was minted and then removed, not refused before it reached the target.
            Assert.Contains(vault.Log.Lines, line => line.Text.StartsWith("Minted PostgreSQL role", StringComparison.Ordinal)
                && line.Text.Contains("it_pg_af_issue", StringComparison.Ordinal));
            Assert.Contains(vault.Log.Lines, line => line.Text.StartsWith("Dropped PostgreSQL role", StringComparison.Ordinal)
                && line.Text.Contains("it_pg_af_issue", StringComparison.Ordinal));
            Assert.Empty(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_af_issue"));
            Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_destroyed_credential_whose_close_cannot_be_recorded_stays_active_until_a_recorded_sweep_expires_it()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_af_revoke");
        var admin = await vault.AdminClientAsync();
        await DynamicSecretsApi.IssueAsync(admin, roleId);
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        var token = await DynamicSecretsApi.TokenAsync(admin);

        await failure.RejectAuditWritesAsync();
        try
        {
            using var revoked = await DynamicSecretsApi.RevokeAsync(admin, lease.Id, token);

            Assert.Equal(
                "The credential was destroyed, but the vault couldn't record it in the audit trail. Its lease shows Active until it expires.",
                (await DynamicSecretsApi.AssertProblemAsync(revoked, 503, "revoked_unrecorded")).GetProperty("title").GetString());
            Assert.Empty(await DynamicSecretsApi.LoginsOfRoleAsync(vault, "it_dyn_af_revoke"));
            Assert.Equal(LeaseStatus.Active, Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).Status);

            await DynamicSecretsApi.ExpireAtAsync(vault, lease.Id, DateTimeOffset.UtcNow.AddSeconds(-1));
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => DynamicSecretsApi.SweepAsync(vault));

            Assert.Equal(LeaseStatus.Active, Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).Status);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await DynamicSecretsApi.SweepAsync(vault);

        Assert.Equal(LeaseStatus.Expired, Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).Status);
    }

    [Fact]
    public async Task A_sweep_whose_retry_of_a_one_shot_revocation_fails_closes_the_lease_as_revocation_failed()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_af_one_shot", revocation: DynamicSecretsApi.OneShotRevocation);
        var admin = await vault.AdminClientAsync();
        await DynamicSecretsApi.IssueAsync(admin, roleId);
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        var token = await DynamicSecretsApi.TokenAsync(admin);

        await failure.RejectAuditWritesAsync();
        try
        {
            await DynamicSecretsApi.RevokeAsync(admin, lease.Id, token);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
        await DynamicSecretsApi.ExpireAtAsync(vault, lease.Id, DateTimeOffset.UtcNow.AddSeconds(-1));

        await DynamicSecretsApi.SweepAsync(vault);

        var closed = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        Assert.Equal(LeaseStatus.RevocationFailed, closed.Status);
        Assert.Equal("The backend refused to destroy the credential; it may still work.", closed.RevocationError);
        Assert.Empty(await DynamicSecretsApi.LoginsOfRoleAsync(vault, "it_dyn_af_one_shot"));
    }
}
