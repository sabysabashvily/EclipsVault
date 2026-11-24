using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog.Events;
using Testcontainers.PostgreSql;
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
            var serverLog = await vault.PostgresDynamicTargetLogsAsync();
            Assert.Contains(serverLog.Split('\n'), line =>
                line.Contains($"ALTER ROLE \"{role}\" WITH PASSWORD 'SCRAM-SHA-256$4096:", StringComparison.Ordinal));
            Assert.DoesNotContain(stored, serverLog, StringComparison.Ordinal);
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
        const string initialValue = "ItPgManagedMissingValue1";
        var details = await SecretSharing.CreateSecretAsync(admin, "it-pg-managed-missing", value: initialValue);
        await SecretDetailsApi.MakeManagedAsync(vault, details, "it_pg_no_such_managed_role", DynamicSecretBackend.Postgres);
        var before = await AuditTrail.HeadAsync(vault);
        var logStart = vault.Log.Lines.Count;

        var response = await SecretDetailsApi.RotateUpstreamAsync(admin, details);

        var problem = await SecretsApi.AssertProblemAsync(response, 502, "upstream_refused");
        Assert.Equal("The backend refused the rotation. The credential is unchanged.", problem.GetProperty("title").GetString());
        Assert.Empty(await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
        Assert.Equal(initialValue, await UpstreamRotationTests.StoredValueAsync(vault, details));
        var newLogs = vault.Log.Lines.Skip(logStart).ToList();
        Assert.DoesNotContain(newLogs, line => MintedPassword().IsMatch(line.Text));
        Assert.DoesNotContain(newLogs, line => line.Text.Contains("it_pg_no_such_managed_role", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Managed_rotation_refuses_an_overlong_postgres_role_binding_without_touching_the_truncated_prefix_role()
    {
        var admin = await vault.AdminClientAsync();
        var password = ManagedTargetLogins.NewPassword();
        var prefixRole63 = "it_pg_trunc_" + new string('a', 51);
        Assert.Equal(63, prefixRole63.Length);
        var overlongRole64 = prefixRole63 + "b";
        await PostgresDynamicTarget.CreateManagedRoleAsync(vault, prefixRole63, password);
        try
        {
            var details = await SecretSharing.CreateSecretAsync(admin, "it-pg-managed-overlong", value: password);
            await SecretDetailsApi.MakeManagedAsync(vault, details, overlongRole64, DynamicSecretBackend.Postgres);

            var response = await SecretDetailsApi.RotateUpstreamAsync(admin, details);

            await SecretsApi.AssertProblemAsync(response, 502, "upstream_refused");
            Assert.Equal(password, await UpstreamRotationTests.StoredValueAsync(vault, details));
            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, prefixRole63, password));
        }
        finally
        {
            await PostgresDynamicTarget.DropRoleAsync(vault, prefixRole63);
        }
    }

    [Fact]
    public async Task Managed_rotation_uses_the_server_role_length_limit_when_the_search_path_shadows_current_setting()
    {
        const string prefixRole = "it_pg_shadow_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var password = ManagedTargetLogins.NewPassword();
        await PostgresDynamicTarget.CreateManagedRoleAsync(vault, prefixRole, password);
        var target = new NpgsqlConnectionStringBuilder(vault.PostgresDynamicTargetConnectionString);
        await using var setup = new NpgsqlConnection(target.ConnectionString);
        await setup.OpenAsync();
        await using var create = new NpgsqlCommand("""
            CREATE FUNCTION public.current_setting(text) RETURNS text LANGUAGE sql AS $$ SELECT '128'::text $$;
            """, setup);
        await create.ExecuteNonQueryAsync();
        try
        {
            target.SearchPath = "public,pg_catalog";
            var backend = new PostgresBackend(Options.Create(new DynamicSecretTargetOptions
            {
                PostgresTargetConnectionString = target.ConnectionString,
            }), TimeProvider.System, NullLogger<PostgresBackend>.Instance);

            await Assert.ThrowsAsync<UpstreamRotationRefusedException>(() => backend.RotatePrincipalAsync(
                prefixRole + "b", ManagedTargetLogins.NewPassword(), CancellationToken.None));

            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, prefixRole, password));
        }
        finally
        {
            await using var drop = new NpgsqlCommand("DROP FUNCTION public.current_setting(text);", setup);
            await drop.ExecuteNonQueryAsync();
            await PostgresDynamicTarget.DropRoleAsync(vault, prefixRole);
        }
    }

    [Fact]
    public async Task Cancelling_the_vault_cluster_probe_refuses_managed_rotation_before_sending_a_password_change()
    {
        const string role = "it_pg_rotation_cancelled";
        var password = ManagedTargetLogins.NewPassword();
        await PostgresDynamicTarget.CreateManagedRoleAsync(vault, role, password);
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var vaultEndpoint = new NpgsqlConnectionStringBuilder(vault.PostgresDynamicTargetConnectionString)
            {
                Host = IPAddress.Loopback.ToString(),
                Port = ((IPEndPoint)listener.LocalEndpoint).Port,
                Pooling = false,
            };
            using var provider = BuildDiPostgresVault(vaultEndpoint.ConnectionString,
                vault.PostgresDynamicTargetConnectionString!, NullLogger<PostgresBackend>.Instance);
            using var scope = provider.CreateScope();
            var backend = scope.ServiceProvider.GetServices<IManagedSecretBackend>()
                .Single(candidate => candidate.Backend == DynamicSecretBackend.Postgres);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var rotation = backend.RotatePrincipalAsync(role, ManagedTargetLogins.NewPassword(), cancel.Token);

            using var probe = await listener.AcceptTcpClientAsync(cancel.Token);
            await cancel.CancelAsync();

            await Assert.ThrowsAsync<UpstreamRotationRefusedException>(() => rotation);
            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, role, password));
        }
        finally
        {
            await PostgresDynamicTarget.DropRoleAsync(vault, role);
        }
    }

    [Fact]
    public async Task Managed_rotation_refuses_before_writing_when_the_stored_password_contains_punctuation_unusable_for_compensation()
    {
        var admin = await vault.AdminClientAsync();
        const string punctuatedPassword = "Old!Pass#2026-Value";
        const string role = "it_pg_managed_punct";
        await PostgresDynamicTarget.CreateManagedRoleAsync(vault, role, punctuatedPassword);
        try
        {
            var details = await SecretSharing.CreateSecretAsync(admin, "it-pg-managed-punct", value: punctuatedPassword);
            await SecretDetailsApi.MakeManagedAsync(vault, details, role, DynamicSecretBackend.Postgres);
            var before = await AuditTrail.HeadAsync(vault);

            var response = await SecretDetailsApi.RotateUpstreamAsync(admin, details);

            await SecretsApi.AssertProblemAsync(response, 502, "upstream_refused");
            Assert.Empty(await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
            Assert.Equal(punctuatedPassword, await UpstreamRotationTests.StoredValueAsync(vault, details));
            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, role, punctuatedPassword));
        }
        finally
        {
            await PostgresDynamicTarget.DropRoleAsync(vault, role);
        }
    }

    [Fact]
    public async Task A_competing_postgres_managed_rotation_is_refused_with_change_in_progress_and_the_stored_value_wins()
    {
        var admin = await vault.AdminClientAsync();
        var password = ManagedTargetLogins.NewPassword();
        const string role = "it_pg_managed_race";
        await PostgresDynamicTarget.CreateManagedRoleAsync(vault, role, password);
        try
        {
            var details = await SecretSharing.CreateSecretAsync(admin, "it-pg-managed-race", value: password);
            await SecretDetailsApi.MakeManagedAsync(vault, details, role, DynamicSecretBackend.Postgres);
            // Warm up once so any one-time evaluation-window write does not queue behind HeldAuditChain.
            SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateUpstreamAsync(admin, details));
            var secretId = SecretSharing.SecretIdOf(details);

            Task first;
            await using (var held = await HeldAuditChain.TakeAsync(vault))
            {
                first = Task.Run(async () =>
                {
                    using var scope = vault.Services.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ISecretService>().RotateManagedAsync(secretId, null, default);
                });
                await held.WaitForQueuedWritersAsync(1);

                var competing = await SecretDetailsApi.RotateUpstreamAsync(admin, details);
                await SecretsApi.AssertProblemAsync(competing, 409, "change_in_progress");
            }

            await first;
            var finalValue = await UpstreamRotationTests.StoredValueAsync(vault, details);
            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, role, finalValue));
        }
        finally
        {
            await PostgresDynamicTarget.DropRoleAsync(vault, role);
        }
    }

    [Fact]
    public async Task A_lost_response_after_altering_a_postgres_role_compensates_and_restores_the_previous_password()
    {
        var admin = await vault.AdminClientAsync();
        var initialPassword = ManagedTargetLogins.NewPassword();
        const string role = "it_pg_managed_comp";
        await PostgresDynamicTarget.CreateManagedRoleAsync(vault, role, initialPassword);
        try
        {
            var details = await SecretSharing.CreateSecretAsync(admin, "it-pg-managed-comp", value: initialPassword);
            await SecretDetailsApi.MakeManagedAsync(vault, details, role, DynamicSecretBackend.Postgres);
            var secretId = SecretSharing.SecretIdOf(details);

            using var scope = vault.Services.CreateScope();
            var realPostgres = scope.ServiceProvider.GetServices<IManagedSecretBackend>()
                .Single(b => b.Backend == DynamicSecretBackend.Postgres);
            var lostOnce = new FailOnceAfterRotatingBackend(realPostgres);
            var service = new SecretService(
                scope.ServiceProvider.GetRequiredService<ISecretRepository>(),
                scope.ServiceProvider.GetRequiredService<ICryptoEngineFactory>(),
                scope.ServiceProvider.GetRequiredService<ISecretCache>(),
                scope.ServiceProvider.GetRequiredService<IIntrusionResponseService>(),
                scope.ServiceProvider.GetRequiredService<IAuditSink>(),
                scope.ServiceProvider.GetRequiredService<IAuditContext>(),
                [lostOnce],
                TimeProvider.System,
                scope.ServiceProvider.GetRequiredService<IPremiumFeatureUsage>(),
                scope.ServiceProvider.GetRequiredService<IManagedPrincipalLock>());

            await Assert.ThrowsAsync<UpstreamRotationRefusedException>(() =>
                service.RotateManagedAsync(secretId, null, CancellationToken.None));

            Assert.Equal(2, lostOnce.Calls);
            Assert.Equal(initialPassword, await UpstreamRotationTests.StoredValueAsync(vault, details));
            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, role, initialPassword));
            var serverLog = await vault.PostgresDynamicTargetLogsAsync();
            var alterations = serverLog.Split('\n').Where(line =>
                line.Contains($"ALTER ROLE \"{role}\" WITH PASSWORD", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, alterations.Length);
            Assert.All(alterations, line =>
            {
                Assert.Contains("SCRAM-SHA-256$4096:", line, StringComparison.Ordinal);
                Assert.DoesNotContain(initialPassword, line, StringComparison.Ordinal);
            });
        }
        finally
        {
            await PostgresDynamicTarget.DropRoleAsync(vault, role);
        }
    }

    private sealed class FailOnceAfterRotatingBackend(IManagedSecretBackend inner) : IManagedSecretBackend
    {
        public int Calls { get; private set; }

        public DynamicSecretBackend Backend => inner.Backend;

        public bool CanApplyPassword(ReadOnlySpan<byte> password) => inner.CanApplyPassword(password);

        public async Task RotatePrincipalAsync(string principal, string newPassword, CancellationToken ct)
        {
            Calls++;
            await inner.RotatePrincipalAsync(principal, newPassword, ct);
            if (Calls == 1)
            {
                throw new IOException("Simulated lost reply after PostgreSQL committed ALTER ROLE.");
            }
        }
    }

    [Fact]
    public async Task Managed_rotation_refuses_a_vault_cluster_alias_and_leaves_the_roles_password_unchanged()
    {
        const string role = "it_pg_managed_same_cluster";
        var initialPassword = ManagedTargetLogins.NewPassword();
        await PostgresDynamicTarget.CreateManagedRoleAsync(vault, role, initialPassword);
        var target = new NpgsqlConnectionStringBuilder(vault.PostgresDynamicTargetConnectionString!);
        using var forwarder = new TcpListener(IPAddress.Loopback, 0);
        forwarder.Start();
        using var shutdown = new CancellationTokenSource();
        var relayErrors = new ConcurrentBag<Exception>();
        var acceptedCount = 0;
        var relay = RelayConnectionsAsync(forwarder, target.Host!, target.Port, relayErrors,
            () => Interlocked.Increment(ref acceptedCount), shutdown.Token);
        try
        {
            var alias = new NpgsqlConnectionStringBuilder(target.ConnectionString)
            {
                Host = IPAddress.Loopback.ToString(),
                Port = ((IPEndPoint)forwarder.LocalEndpoint).Port,
                Pooling = false,
            };
            var logger = new CapturingLogger<PostgresBackend>();
            using var provider = BuildDiPostgresVault(target.ConnectionString, alias.ConnectionString, logger);
            using var scope = provider.CreateScope();
            var backend = scope.ServiceProvider.GetServices<IManagedSecretBackend>()
                .Single(candidate => candidate.Backend == DynamicSecretBackend.Postgres);

            await Assert.ThrowsAsync<UpstreamRotationRefusedException>(() =>
                backend.RotatePrincipalAsync(role, ManagedTargetLogins.NewPassword(), CancellationToken.None));

            Assert.True(acceptedCount > 0);
            Assert.Contains(logger.Messages, message => message.Contains("own PostgreSQL cluster", StringComparison.Ordinal));
            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, role, initialPassword));
        }
        finally
        {
            await shutdown.CancelAsync();
            forwarder.Stop();
            await relay;
            await PostgresDynamicTarget.DropRoleAsync(vault, role);
        }

        Assert.Empty(relayErrors);
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

    [Fact]
    public async Task Minting_sends_a_scram_verifier_so_target_ddl_logs_never_record_the_plaintext_password()
    {
        var roleId = await PostgresDynamicTarget.AddRoleAsync(vault, "it_pg_scram_log");
        var admin = await vault.AdminClientAsync();

        var (username, password) = await DynamicSecretsApi.IssuedCredentialAsync(await DynamicSecretsApi.IssueAsync(admin, roleId));
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        try
        {
            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, username, password));
            var serverLog = await fixture.Vault.PostgresDynamicTargetLogsAsync();
            Assert.Contains(
                serverLog.Split('\n'),
                line => line.Contains($"CREATE ROLE \"{username}\"", StringComparison.Ordinal)
                    && line.Contains("SCRAM-SHA-256$4096:", StringComparison.Ordinal));
            Assert.DoesNotContain(password, serverLog, StringComparison.Ordinal);
        }
        finally
        {
            await DynamicSecretsApi.RevokeAsync(admin, lease.Id);
        }
    }

    [Fact]
    public async Task Minting_refuses_when_the_target_reaches_the_same_postgres_cluster_through_a_different_endpoint()
    {
        var target = new NpgsqlConnectionStringBuilder(vault.PostgresDynamicTargetConnectionString!);
        using var forwarder = new TcpListener(IPAddress.Loopback, 0);
        forwarder.Start();
        var forwardedPort = ((IPEndPoint)forwarder.LocalEndpoint).Port;
        using var shutdown = new CancellationTokenSource();
        var relayErrors = new ConcurrentBag<Exception>();
        var acceptedCount = 0;
        var relay = RelayConnectionsAsync(forwarder, target.Host!, target.Port, relayErrors, () => Interlocked.Increment(ref acceptedCount), shutdown.Token);
        try
        {
            var viaDifferentPort = new NpgsqlConnectionStringBuilder(target.ConnectionString)
            {
                Host = IPAddress.Loopback.ToString(),
                Port = forwardedPort,
                Pooling = false,
            };
            var logger = new CapturingLogger<PostgresBackend>();
            using var provider = BuildDiPostgresVault(target.ConnectionString, viaDifferentPort.ConnectionString, logger);
            using var scope = provider.CreateScope();
            var backend = scope.ServiceProvider.GetServices<IDynamicSecretBackend>()
                .Single(candidate => candidate.Backend == DynamicSecretBackend.Postgres);
            var role = new DynamicSecretRole
            {
                Name = "it_pg_same_cluster",
                Backend = DynamicSecretBackend.Postgres,
                CreationStatements = DbSeeder.PostgresReaderRecipe,
                RevocationStatements = DbSeeder.PostgresRevocation,
            };

            await Assert.ThrowsAsync<MintRefusedException>(() =>
                backend.MintAsync(role, "ev_it_pg_same_cluster_1", "Abc123Def456Ghi789Jkl012Mno345Pqr678Stu9",
                    DateTimeOffset.UtcNow.AddMinutes(10), CancellationToken.None));

            Assert.True(acceptedCount > 0, "The TCP forwarder never received a connection from PostgresBackend.");
            Assert.Contains(logger.Messages, message =>
                message.Contains("own PostgreSQL cluster", StringComparison.Ordinal));
            Assert.Empty(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_same_cluster"));
        }
        finally
        {
            await shutdown.CancelAsync();
            forwarder.Stop();
            await relay;
        }

        Assert.Empty(relayErrors);
    }

    [Fact]
    public async Task Minting_refuses_same_cluster_even_when_search_path_shadows_pg_control_system()
    {
        var target = new NpgsqlConnectionStringBuilder(vault.PostgresDynamicTargetConnectionString!);
        await using (var setup = new NpgsqlConnection(target.ConnectionString))
        {
            await setup.OpenAsync();
            await using var createShadow = new NpgsqlCommand(
                "CREATE OR REPLACE FUNCTION public.pg_control_system() RETURNS TABLE(system_identifier bigint) LANGUAGE sql AS $$ SELECT 999999999999::bigint $$;",
                setup);
            await createShadow.ExecuteNonQueryAsync();
        }

        using var forwarder = new TcpListener(IPAddress.Loopback, 0);
        forwarder.Start();
        var forwardedPort = ((IPEndPoint)forwarder.LocalEndpoint).Port;
        using var shutdown = new CancellationTokenSource();
        var relayErrors = new ConcurrentBag<Exception>();
        var acceptedCount = 0;
        var relay = RelayConnectionsAsync(forwarder, target.Host!, target.Port, relayErrors, () => Interlocked.Increment(ref acceptedCount), shutdown.Token);
        try
        {
            var viaShadowedSearchPath = new NpgsqlConnectionStringBuilder(target.ConnectionString)
            {
                Host = IPAddress.Loopback.ToString(),
                Port = forwardedPort,
                SearchPath = "public,pg_catalog",
                Pooling = false,
            };
            var logger = new CapturingLogger<PostgresBackend>();
            using var provider = BuildDiPostgresVault(target.ConnectionString, viaShadowedSearchPath.ConnectionString, logger);
            using var scope = provider.CreateScope();
            var backend = scope.ServiceProvider.GetServices<IDynamicSecretBackend>()
                .Single(candidate => candidate.Backend == DynamicSecretBackend.Postgres);
            var role = new DynamicSecretRole
            {
                Name = "it_pg_shadowed",
                Backend = DynamicSecretBackend.Postgres,
                CreationStatements = DbSeeder.PostgresReaderRecipe,
                RevocationStatements = DbSeeder.PostgresRevocation,
            };

            await Assert.ThrowsAsync<MintRefusedException>(() =>
                backend.MintAsync(role, "ev_it_pg_shadowed_1", "Abc123Def456Ghi789Jkl012Mno345Pqr678Stu9",
                    DateTimeOffset.UtcNow.AddMinutes(10), CancellationToken.None));

            Assert.True(acceptedCount > 0, "The TCP forwarder never received a connection from PostgresBackend.");
            Assert.Contains(logger.Messages, message =>
                message.Contains("own PostgreSQL cluster", StringComparison.Ordinal));
            Assert.Empty(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_shadowed"));
        }
        finally
        {
            await shutdown.CancelAsync();
            forwarder.Stop();
            await relay;
            await using var cleanup = new NpgsqlConnection(target.ConnectionString);
            await cleanup.OpenAsync();
            await using var dropShadow = new NpgsqlCommand("DROP FUNCTION IF EXISTS public.pg_control_system();", cleanup);
            await dropShadow.ExecuteNonQueryAsync();
        }

        Assert.Empty(relayErrors);
    }

    [Fact]
    public async Task Minting_succeeds_against_a_distinct_cluster_when_a_multi_host_vault_dsn_sets_target_session_attributes_read_write()
    {
        var vaultConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.Equals(Environment.GetEnvironmentVariable("Database__Provider"), "Postgres", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(vaultConnectionString))
        {
            // When the vault runs on SQL Server, prove multi-host Target Session Attributes=read-write
            // still probes cleanly without Npgsql single-host validation errors on a same-cluster alias.
            var sameTarget = new NpgsqlConnectionStringBuilder(vault.PostgresDynamicTargetConnectionString!);
            using var forwarder = new TcpListener(IPAddress.Loopback, 0);
            forwarder.Start();
            var forwardedPort = ((IPEndPoint)forwarder.LocalEndpoint).Port;
            using var shutdown = new CancellationTokenSource();
            var relayErrors = new ConcurrentBag<Exception>();
            var acceptedCount = 0;
            var relay = RelayConnectionsAsync(forwarder, sameTarget.Host!, sameTarget.Port, relayErrors, () => Interlocked.Increment(ref acceptedCount), shutdown.Token);
            try
            {
                var multiHostVault = new NpgsqlConnectionStringBuilder(sameTarget.ConnectionString)
                {
                    Host = $"{sameTarget.Host}:{sameTarget.Port},{sameTarget.Host}:{sameTarget.Port}",
                    TargetSessionAttributes = "read-write",
                    Pooling = false,
                };
                var viaAlias = new NpgsqlConnectionStringBuilder(sameTarget.ConnectionString)
                {
                    Host = IPAddress.Loopback.ToString(),
                    Port = forwardedPort,
                    Pooling = false,
                };
                var logger = new CapturingLogger<PostgresBackend>();
                using var provider = BuildDiPostgresVault(multiHostVault.ConnectionString, viaAlias.ConnectionString, logger);
                using var scope = provider.CreateScope();
                var backend = scope.ServiceProvider.GetServices<IDynamicSecretBackend>()
                    .Single(candidate => candidate.Backend == DynamicSecretBackend.Postgres);
                var role = new DynamicSecretRole
                {
                    Name = "it_pg_multihost_rw",
                    Backend = DynamicSecretBackend.Postgres,
                    CreationStatements = DbSeeder.PostgresReaderRecipe,
                    RevocationStatements = DbSeeder.PostgresRevocation,
                };

                await Assert.ThrowsAsync<MintRefusedException>(() =>
                    backend.MintAsync(role, "ev_it_pg_multihost_rw_1", "Abc123Def456Ghi789Jkl012Mno345Pqr678Stu9",
                        DateTimeOffset.UtcNow.AddMinutes(10), CancellationToken.None));

                Assert.True(acceptedCount > 0);
                Assert.Contains(logger.Messages, message => message.Contains("own PostgreSQL cluster", StringComparison.Ordinal));
            }
            finally
            {
                await shutdown.CancelAsync();
                forwarder.Stop();
                await relay;
            }

            Assert.Empty(relayErrors);
            return;
        }

        var vaultBuilder = new NpgsqlConnectionStringBuilder(vaultConnectionString);
        var multiHostReadWriteVault = new NpgsqlConnectionStringBuilder(vaultBuilder.ConnectionString)
        {
            Host = $"{vaultBuilder.Host}:{vaultBuilder.Port},{vaultBuilder.Host}:{vaultBuilder.Port}",
            TargetSessionAttributes = "read-write",
            Pooling = false,
        };
        var distinctLogger = new CapturingLogger<PostgresBackend>();
        using var distinctProvider = BuildDiPostgresVault(
            multiHostReadWriteVault.ConnectionString,
            vault.PostgresDynamicTargetConnectionString!,
            distinctLogger);
        using var distinctScope = distinctProvider.CreateScope();
        var distinctBackend = distinctScope.ServiceProvider.GetServices<IDynamicSecretBackend>()
            .Single(candidate => candidate.Backend == DynamicSecretBackend.Postgres);
        var distinctRole = new DynamicSecretRole
        {
            Name = "it_pg_multihost_distinct",
            Backend = DynamicSecretBackend.Postgres,
            CreationStatements = DbSeeder.PostgresReaderRecipe,
            RevocationStatements = DbSeeder.PostgresRevocation,
        };
        const string mintedUser = "ev_it_pg_multihost_distinct_1";
        const string mintedPassword = "Abc123Def456Ghi789Jkl012Mno345Pqr678Stu9";
        try
        {
            await distinctBackend.MintAsync(distinctRole, mintedUser, mintedPassword, DateTimeOffset.UtcNow.AddMinutes(10), CancellationToken.None);
            Assert.True(await PostgresDynamicTarget.OpensWithAsync(vault, mintedUser, mintedPassword));
        }
        finally
        {
            await distinctBackend.RevokeAsync(distinctRole, mintedUser, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Minting_refuses_when_only_the_second_vault_host_shares_the_targets_cluster()
    {
        var target = new NpgsqlConnectionStringBuilder(vault.PostgresDynamicTargetConnectionString!);
        await using var otherCluster = new PostgreSqlBuilder(PostgresShareIssuerBackfill.PostgresImage)
            .WithDatabase(target.Database!)
            .WithUsername(target.Username!)
            .WithPassword(target.Password!)
            .Build();
        await otherCluster.StartAsync();
        var other = new NpgsqlConnectionStringBuilder(otherCluster.GetConnectionString());

        using var forwarder = new TcpListener(IPAddress.Loopback, 0);
        forwarder.Start();
        var forwardedPort = ((IPEndPoint)forwarder.LocalEndpoint).Port;
        using var shutdown = new CancellationTokenSource();
        var relayErrors = new ConcurrentBag<Exception>();
        var acceptedCount = 0;
        var relay = RelayConnectionsAsync(forwarder, target.Host!, target.Port, relayErrors, () => Interlocked.Increment(ref acceptedCount), shutdown.Token);
        try
        {
            // First vault host is a distinct cluster; only the second vault host matches the dynamic target's cluster.
            var twoClusterVault = new NpgsqlConnectionStringBuilder(target.ConnectionString)
            {
                Host = $"{other.Host}:{other.Port},{target.Host}:{target.Port}",
                Pooling = false,
            };
            var viaAlias = new NpgsqlConnectionStringBuilder(target.ConnectionString)
            {
                Host = IPAddress.Loopback.ToString(),
                Port = forwardedPort,
                Pooling = false,
            };
            var logger = new CapturingLogger<PostgresBackend>();
            using var provider = BuildDiPostgresVault(twoClusterVault.ConnectionString, viaAlias.ConnectionString, logger);
            using var scope = provider.CreateScope();
            var backend = scope.ServiceProvider.GetServices<IDynamicSecretBackend>()
                .Single(candidate => candidate.Backend == DynamicSecretBackend.Postgres);
            var role = new DynamicSecretRole
            {
                Name = "it_pg_second_host",
                Backend = DynamicSecretBackend.Postgres,
                CreationStatements = DbSeeder.PostgresReaderRecipe,
                RevocationStatements = DbSeeder.PostgresRevocation,
            };

            await Assert.ThrowsAsync<MintRefusedException>(() =>
                backend.MintAsync(role, "ev_it_pg_second_host_1", "Abc123Def456Ghi789Jkl012Mno345Pqr678Stu9",
                    DateTimeOffset.UtcNow.AddMinutes(10), CancellationToken.None));

            Assert.True(acceptedCount > 0, "The TCP forwarder never received a connection from PostgresBackend.");
            Assert.Contains(logger.Messages, message =>
                message.Contains("own PostgreSQL cluster", StringComparison.Ordinal));
            Assert.Empty(await PostgresDynamicTarget.RolesOfRoleAsync(vault, "it_pg_second_host"));
        }
        finally
        {
            await shutdown.CancelAsync();
            forwarder.Stop();
            await relay;
        }

        Assert.Empty(relayErrors);
    }

    private static ServiceProvider BuildDiPostgresVault(
        string vaultConnectionString,
        string dynamicTargetConnectionString,
        ILogger<PostgresBackend> logger)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Postgres",
                ["ConnectionStrings:DefaultConnection"] = vaultConnectionString,
                ["DynamicSecrets:PostgresTargetConnectionString"] = dynamicTargetConnectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEclipsVaultInfrastructure(configuration);
        services.AddSingleton(logger);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Enqueue($"{formatter(state, exception)} {exception}");
    }

    private static async Task RelayConnectionsAsync(
        TcpListener listener,
        string destinationHost,
        int destinationPort,
        ConcurrentBag<Exception> errors,
        Action onAccepted,
        CancellationToken ct)
    {
        var workers = new List<Task>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                onAccepted();
                workers.Add(Task.Run(async () =>
                {
                    try
                    {
                        using (client)
                        using (var upstream = new TcpClient())
                        {
                            await upstream.ConnectAsync(destinationHost, destinationPort, ct);
                            var clientStream = client.GetStream();
                            var upstreamStream = upstream.GetStream();
                            using var halfClose = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            var clientToUpstream = clientStream.CopyToAsync(upstreamStream, halfClose.Token);
                            var upstreamToClient = upstreamStream.CopyToAsync(clientStream, halfClose.Token);
                            var first = await Task.WhenAny(clientToUpstream, upstreamToClient);
                            await first;
                            await halfClose.CancelAsync();
                            client.Close();
                            upstream.Close();
                            var second = ReferenceEquals(first, clientToUpstream) ? upstreamToClient : clientToUpstream;
                            try
                            {
                                await second;
                            }
                            catch (OperationCanceledException)
                            {
                            }
                            catch (IOException)
                            {
                            }
                            catch (ObjectDisposedException)
                            {
                            }
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                    }
                    catch (IOException) when (ct.IsCancellationRequested)
                    {
                    }
                    catch (SocketException) when (ct.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }, CancellationToken.None));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
        finally
        {
            await Task.WhenAll(workers);
        }
    }

    /// <summary>A minted password's shape: 40 letters and digits, standing alone.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z0-9]{40}(?![A-Za-z0-9])")]
    private static partial Regex MintedPassword();
}
