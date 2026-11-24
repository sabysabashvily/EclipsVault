using System.Text.Json;
using System.Text.RegularExpressions;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        // The recipe writes the lease's end to the whole second.
        var validUntil = await PostgresDynamicTarget.ValidUntilAsync(vault, username);
        Assert.NotNull(validUntil);
        Assert.Equal(lease.ExpiresAtUtc, validUntil.Value, TimeSpan.FromSeconds(1));

        using var revoked = await DynamicSecretsApi.RevokeAsync(admin, lease.Id);

        var answer = await DynamicSecretsApi.JsonAsync(revoked);
        Assert.Equal(("Revoked", JsonValueKind.Null), (answer.GetProperty("status").GetString(), answer.GetProperty("revocationNote").ValueKind));
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
    public async Task Handing_back_a_role_that_owns_a_table_fails_with_a_critical_row_and_keeps_the_table_as_it_was()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_owner", creation: """
            CREATE ROLE "{{name}}" LOGIN PASSWORD '{{password}}' IN ROLE pg_read_all_data;
            GRANT CREATE ON SCHEMA public TO "{{name}}";
            """);
        var admin = await vault.AdminClientAsync();
        var (username, password) = await DynamicSecretsApi.IssuedCredentialAsync(await DynamicSecretsApi.IssueAsync(admin, roleId));
        await using var session = await PostgresDynamicTarget.SignInAsync(vault, username, password);
        await using (var create = new Npgsql.NpgsqlCommand("CREATE TABLE it_pg_owned (id int)", session))
        {
            await create.ExecuteNonQueryAsync();
        }
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        var before = await AuditTrail.HeadAsync(vault);
        try
        {
            using var revoked = await DynamicSecretsApi.RevokeAsync(admin, lease.Id);

            Assert.Equal("RevocationFailed", (await DynamicSecretsApi.JsonAsync(revoked)).GetProperty("status").GetString());
            var failure = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.ResourceId == lease.Id);
            Assert.Equal((AuditAction.DynamicCredentialRevocationFailed, true), (failure.Action, failure.IsCritical));
            Assert.Equal(username, await PostgresDynamicTarget.TableOwnerAsync(vault, "it_pg_owned"));
            Assert.False(await PostgresDynamicTarget.StillWorksAsync(session));
            Assert.False(await PostgresDynamicTarget.CanSignInAsync(vault, username));
        }
        finally
        {
            // What an operator does once they have decided the table can go.
            await PostgresDynamicTarget.DropRoleAndWhatItOwnsAsync(vault, username);
        }
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
    public async Task A_revoke_the_target_refuses_closes_the_lease_as_revocation_failed_with_a_critical_row_and_the_credential_no_longer_signs_in()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_revoke_refused", revocation: PostgresDynamicTarget.RefusedRevocation);
        var admin = await vault.AdminClientAsync();
        var (username, password) = await DynamicSecretsApi.IssuedCredentialAsync(await DynamicSecretsApi.IssueAsync(admin, roleId));
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        var before = await AuditTrail.HeadAsync(vault);
        try
        {
            using var revoked = await DynamicSecretsApi.RevokeAsync(admin, lease.Id);

            var answer = await DynamicSecretsApi.JsonAsync(revoked);
            Assert.Equal(("RevocationFailed", "The backend refused to destroy the credential; it may still work."),
                (answer.GetProperty("status").GetString(), answer.GetProperty("revocationNote").GetString()));
            Assert.Equal(LeaseStatus.RevocationFailed, Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).Status);
            var failure = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.ResourceId == lease.Id);
            Assert.Equal((AuditAction.DynamicCredentialRevocationFailed, true), (failure.Action, failure.IsCritical));
            Assert.Single(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_revoke_refused"));
            Assert.False(await PostgresDynamicTarget.OpensWithAsync(vault, username, password));
            Assert.DoesNotContain(vault.Log.Lines, line => line.Text.Contains("Revocation refused.", StringComparison.Ordinal));
        }
        finally
        {
            await PostgresDynamicTarget.DropRoleAsync(vault, username);
        }
    }

    [Fact]
    public async Task A_recipe_that_only_drops_the_role_still_has_the_roles_open_session_ended()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_drop_only", revocation: PostgresDynamicTarget.DropOnlyRevocation);
        var admin = await vault.AdminClientAsync();
        var (username, password) = await DynamicSecretsApi.IssuedCredentialAsync(await DynamicSecretsApi.IssueAsync(admin, roleId));
        await using var session = await PostgresDynamicTarget.SignInAsync(vault, username, password);
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));

        using var revoked = await DynamicSecretsApi.RevokeAsync(admin, lease.Id);

        Assert.Equal("Revoked", (await DynamicSecretsApi.JsonAsync(revoked)).GetProperty("status").GetString());
        Assert.False(await PostgresDynamicTarget.StillWorksAsync(session));
        Assert.Empty(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_drop_only"));
    }

    [Fact]
    public async Task A_connection_still_starting_up_when_revocation_begins_is_ended_once_it_is_listed()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_late_startup");
        var admin = await vault.AdminClientAsync();
        var (username, password) = await DynamicSecretsApi.IssuedCredentialAsync(await DynamicSecretsApi.IssueAsync(admin, roleId));
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        // Held for up to three seconds, within the seven revocation waits here: authentication_timeout's two plus five of margin.
        var signingIn = PostgresDynamicTarget.StartSigningInAsync(vault, username, password, TimeSpan.FromSeconds(3));
        await PostgresDynamicTarget.WaitForStartupWithoutRoleAsync(vault);

        using var revoked = await DynamicSecretsApi.RevokeAsync(admin, lease.Id);

        Assert.Equal("Revoked", (await DynamicSecretsApi.JsonAsync(revoked)).GetProperty("status").GetString());
        Assert.False(await PostgresDynamicTarget.EndsUpWorkingAsync(signingIn));
        Assert.Empty(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_late_startup"));
    }

    [Fact]
    public async Task A_connection_still_starting_up_past_the_wait_fails_the_revoke_and_leaves_the_role_unable_to_sign_in()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_unconfirmed");
        var admin = await vault.AdminClientAsync();
        var (username, password) = await DynamicSecretsApi.IssuedCredentialAsync(await DynamicSecretsApi.IssueAsync(admin, roleId));
        var signingIn = PostgresDynamicTarget.StartSigningInAsync(vault, username, password, TimeSpan.FromSeconds(10));
        await PostgresDynamicTarget.WaitForStartupWithoutRoleAsync(vault);
        // The vault's own backend, on a clock that has always run out, so the startup above is still unconfirmed.
        // The service closes a lease whose revocation throws as RevocationFailed with a critical row (see the refused revoke).
        var backend = new PostgresBackend(
            Options.Create(new DynamicSecretTargetOptions { PostgresTargetConnectionString = vault.PostgresDynamicTargetConnectionString }),
            new MinuteEveryReadClock(), NullLogger<PostgresBackend>.Instance);
        var role = new DynamicSecretRole { Name = "it_pg_unconfirmed", RevocationStatements = DbSeeder.PostgresRevocation };
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => backend.RevokeAsync(role, username, CancellationToken.None));

            Assert.Single(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_unconfirmed"));
            Assert.False(await PostgresDynamicTarget.CanSignInAsync(vault, username));
            Assert.False(await PostgresDynamicTarget.OpensWithAsync(vault, username, password));
        }
        finally
        {
            // It passed the sign-in check before NOLOGIN, so it may still finish; close it, then let the vault drop the role.
            await PostgresDynamicTarget.EndsUpWorkingAsync(signingIn);
            await DynamicSecretsApi.RevokeAsync(admin, Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).Id);
        }
    }

    /// <summary>A clock a minute further on at every read, so any wait measured with it has run out.</summary>
    private sealed class MinuteEveryReadClock : TimeProvider
    {
        private long _now;

        public override long GetTimestamp() => _now += TimestampFrequency * 60;
    }

    [Fact]
    public async Task Revoking_a_role_the_vault_already_dropped_succeeds_again()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_revoke_twice");
        var admin = await vault.AdminClientAsync();
        var (username, _) = await DynamicSecretsApi.IssuedCredentialAsync(await DynamicSecretsApi.IssueAsync(admin, roleId));
        await DynamicSecretsApi.RevokeAsync(admin, Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).Id);
        using var scope = vault.Services.CreateScope();
        var role = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().DynamicSecretRoles.SingleAsync(row => row.Id == roleId);
        var backend = scope.ServiceProvider.GetServices<IDynamicSecretBackend>().Single(candidate => candidate.Backend == DynamicSecretBackend.Postgres);

        await backend.RevokeAsync(role, username, CancellationToken.None);

        Assert.Empty(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_revoke_twice"));
    }

    [Fact]
    public async Task A_recipe_that_fails_part_way_is_refused_leaving_no_role_and_the_password_nowhere()
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

        using var refused = await DynamicSecretsApi.IssueAsync(admin, roleId);

        // The target rolled the whole batch back, so the vault's clean-up finds no role and nothing to undo,
        // without running the role's own (refusing) revocation.
        var problem = await DynamicSecretsApi.AssertProblemAsync(refused, 502, "mint_refused");
        Assert.Empty(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_part_way"));
        Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        Assert.DoesNotContain(vault.Log.Lines, line => line.Level == LogEventLevel.Fatal && line.Text.Contains("it_pg_part_way", StringComparison.Ordinal));
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

    [Fact]
    public async Task Managed_rotation_updates_a_postgres_role_and_stored_value_together_without_logging_the_password()
    {
        var admin = await vault.AdminClientAsync();
        var password = ManagedTargetLogins.NewPassword();
        const string role = "it_pg_managed_rotated";
        await PostgresDynamicTarget.CreateManagedRoleAsync(vault, role, password);
        try
        {
            var details = await SecretSharing.CreateSecretAsync(admin, "it-pg-managed-rotated", value: password);
            await SecretDetailsApi.MakeManagedAsync(vault, details, role, DynamicSecretBackend.Postgres);
            var before = await AuditTrail.HeadAsync(vault);

            var response = await SecretDetailsApi.RotateUpstreamAsync(admin, details);

            SecretDetailsApi.AssertNoContent(response);
            Assert.Equal([AuditAction.SecretUpstreamRotated], await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
            var stored = await UpstreamRotationTests.StoredValueAsync(vault, details);
            Assert.NotEqual(password, stored);
            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, role, stored));
            Assert.False(await PostgresDynamicTarget.OpensWithAsync(vault, role, password));
            Assert.DoesNotContain(vault.Log.Lines, line => line.Text.Contains(stored, StringComparison.Ordinal));
        }
        finally
        {
            await PostgresDynamicTarget.DropRoleAsync(vault, role);
        }
    }

    [Fact]
    public async Task Managed_rotation_refuses_a_missing_postgres_role_and_leaves_the_stored_value_unchanged()
    {
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-pg-managed-missing", value: "it-pg-managed-missing-value");
        await SecretDetailsApi.MakeManagedAsync(vault, details, "it_pg_no_such_managed_role", DynamicSecretBackend.Postgres);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretDetailsApi.RotateUpstreamAsync(admin, details);

        var problem = await SecretsApi.AssertProblemAsync(response, 502, "upstream_refused");
        Assert.Equal("The backend refused the rotation. The credential is unchanged.", problem.GetProperty("title").GetString());
        Assert.Empty(await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
        Assert.Equal("it-pg-managed-missing-value", await UpstreamRotationTests.StoredValueAsync(vault, details));
    }

    [Fact]
    public async Task Restoring_a_version_of_a_postgres_managed_secret_is_refused_and_keeps_the_live_password()
    {
        var admin = await vault.AdminClientAsync();
        var password = ManagedTargetLogins.NewPassword();
        const string role = "it_pg_managed_restore";
        await PostgresDynamicTarget.CreateManagedRoleAsync(vault, role, password);
        try
        {
            var details = await SecretSharing.CreateSecretAsync(admin, "it-pg-managed-restore", value: password);
            await SecretDetailsApi.MakeManagedAsync(vault, details, role, DynamicSecretBackend.Postgres);
            SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateUpstreamAsync(admin, details));
            var live = await UpstreamRotationTests.StoredValueAsync(vault, details);

            var response = await SecretDetailsApi.RestoreAsync(admin, details, await SecretDetailsApi.NewestVersionAsync(vault, details));

            var problem = await SecretsApi.AssertProblemAsync(response, 409, "restore_managed");
            Assert.Contains(role, problem.GetProperty("title").GetString(), StringComparison.Ordinal);
            Assert.Equal(live, await UpstreamRotationTests.StoredValueAsync(vault, details));
            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, role, live));
        }
        finally
        {
            await PostgresDynamicTarget.DropRoleAsync(vault, role);
        }
    }

    /// <summary>A minted password's shape: 40 letters and digits, standing alone.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z0-9]{40}(?![A-Za-z0-9])")]
    private static partial Regex MintedPassword();
}
