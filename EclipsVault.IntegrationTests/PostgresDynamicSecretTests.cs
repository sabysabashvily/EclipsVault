using System.Text.RegularExpressions;
using EclipsVault.Core.Domain.Enums;
using Serilog.Events;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Issuing, handing back and expiring dynamic credentials through the UI API, against real login roles
/// on the vault's PostgreSQL target.
/// </summary>
[Collection(PostgresDynamicSecretsCollection.Name)]
public sealed partial class PostgresDynamicSecretTests(PostgresDynamicSecretsFixture fixture)
{
    private readonly VaultFixture vault = fixture.Vault;

    [Fact]
    public async Task Issuing_creates_a_working_role_that_expires_with_its_lease_and_handing_it_back_drops_it()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_issue");
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        var (username, password) = await DynamicSecretsApi.IssuedCredentialAsync(await DynamicSecretsApi.IssueAsync(admin, roleId));

        Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, username, password));
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        Assert.Equal((LeaseStatus.Active, username), (lease.Status, lease.CredentialIdentity));
        // The target keeps whole seconds, so the role's own expiry is the lease's, truncated.
        Assert.Equal(lease.ExpiresAtUtc.AddTicks(-(lease.ExpiresAtUtc.Ticks % TimeSpan.TicksPerSecond)),
            await PostgresDynamicTarget.ValidUntilAsync(vault, username));

        using var revoked = await DynamicSecretsApi.RevokeAsync(admin, lease.Id);

        Assert.Equal("""{"status":"Revoked","revocationNote":null}""", (await DynamicSecretsApi.JsonAsync(revoked)).GetRawText());
        Assert.Empty(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_issue"));
        Assert.False(await PostgresDynamicTarget.OpensWithAsync(vault, username, password));
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal(new[] { AuditAction.DynamicCredentialIssued, AuditAction.DynamicCredentialRevoked },
            rows.Where(row => row.ResourceId == lease.Id).Select(row => row.Action));
        Assert.DoesNotContain(rows, row => $"{row.ResourceName} {row.Details}".Contains(password, StringComparison.Ordinal));
        Assert.DoesNotContain(vault.Log.Lines, line => line.Text.Contains(password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_expired_lease_ends_the_roles_open_session_and_drops_it_with_an_expiry_row()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_expire");
        var admin = await vault.AdminClientAsync();
        var (username, password) = await DynamicSecretsApi.IssuedCredentialAsync(await DynamicSecretsApi.IssueAsync(admin, roleId));
        await using var session = await PostgresDynamicTarget.SignInAsync(vault, username, password);
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        var before = await AuditTrail.HeadAsync(vault);

        await DynamicSecretsApi.ExpireAtAsync(vault, lease.Id, DateTimeOffset.UtcNow.AddSeconds(-1));
        await DynamicSecretsApi.SweepAsync(vault);

        Assert.Equal(LeaseStatus.Expired, Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).Status);
        Assert.False(await PostgresDynamicTarget.StillWorksAsync(session));
        Assert.Empty(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_expire"));
        Assert.False(await PostgresDynamicTarget.OpensWithAsync(vault, username, password));
        Assert.Contains(await AuditTrail.RowsAfterAsync(vault, before),
            row => row.Action == AuditAction.DynamicCredentialExpired && row.ResourceId == lease.Id);
    }

    [Fact]
    public async Task The_page_lists_a_postgres_role_with_its_backend()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_listed");

        var page = await DynamicSecretsApi.GetAsync(await vault.AdminClientAsync());

        var role = Assert.Single(page.GetProperty("roles").EnumerateArray(), role => role.GetProperty("roleId").GetGuid() == roleId);
        Assert.Equal("Postgres", role.GetProperty("backend").GetString());
    }

    [Fact]
    public async Task A_revoke_the_target_refuses_closes_the_lease_as_revocation_failed_with_a_critical_row()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_revoke_refused", revocation: PostgresDynamicTarget.RefusedRevocation);
        var admin = await vault.AdminClientAsync();
        var (username, password) = await DynamicSecretsApi.IssuedCredentialAsync(await DynamicSecretsApi.IssueAsync(admin, roleId));
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        var before = await AuditTrail.HeadAsync(vault);
        try
        {
            using var revoked = await DynamicSecretsApi.RevokeAsync(admin, lease.Id);

            Assert.Equal("""{"status":"RevocationFailed","revocationNote":"The backend refused to destroy the credential; it may still work."}""",
                (await DynamicSecretsApi.JsonAsync(revoked)).GetRawText());
            Assert.Equal(LeaseStatus.RevocationFailed, Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).Status);
            var failure = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.ResourceId == lease.Id);
            Assert.Equal((AuditAction.DynamicCredentialRevocationFailed, true), (failure.Action, failure.IsCritical));
            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, username, password));
            Assert.DoesNotContain(vault.Log.Lines, line => line.Text.Contains("Revocation refused.", StringComparison.Ordinal));
        }
        finally
        {
            await PostgresDynamicTarget.DropRoleAsync(vault, username);
        }
    }

    [Fact]
    public async Task A_recipe_that_fails_part_way_leaves_no_role_and_no_password_anywhere_even_when_its_revocation_fails()
    {
        // PostgreSQL's error for this GRANT names the missing role, which here is the password.
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_part_way",
            creation: """
                CREATE ROLE "{{name}}" LOGIN PASSWORD '{{password}}';
                GRANT "{{password}}" TO "{{name}}";
                """,
            revocation: PostgresDynamicTarget.RefusedRevocation);
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        using var incomplete = await DynamicSecretsApi.IssueAsync(admin, roleId);

        // The vault cannot see that the target rolled the batch back, so it reports the worst case.
        var problem = await DynamicSecretsApi.AssertProblemAsync(incomplete, 502, "mint_incomplete");
        Assert.Empty(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_part_way"));
        Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        Assert.Contains(vault.Log.Lines, line => line.Level == LogEventLevel.Fatal && line.Text.Contains("it_pg_part_way", StringComparison.Ordinal));
        Assert.DoesNotMatch(MintedPassword(), problem.GetRawText());
        Assert.DoesNotContain(vault.Log.Lines, line => MintedPassword().IsMatch(line.Text));
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => MintedPassword().IsMatch($"{row.ResourceName} {row.Details}"));
    }

    [Fact]
    public async Task A_sql_server_role_on_a_vault_with_only_a_postgres_target_is_refused_and_issues_nothing()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_pg_no_sql_target");
        var admin = await vault.AdminClientAsync();

        using var refused = await DynamicSecretsApi.IssueAsync(admin, roleId);

        Assert.Equal("The backend refused to mint a credential for 'it_pg_no_sql_target'. Nothing was issued.",
            (await DynamicSecretsApi.AssertProblemAsync(refused, 502, "mint_refused")).GetProperty("title").GetString());
        Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
    }

    /// <summary>A minted password's shape: 40 letters and digits, standing alone.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z0-9]{40}(?![A-Za-z0-9])")]
    private static partial Regex MintedPassword();
}
