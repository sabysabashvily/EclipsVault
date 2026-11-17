using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Issuing and revoking dynamic credentials through the UI API, against real SQL Server logins on the
/// vault's own target database.
/// </summary>
[Collection(DynamicSecretsCollection.Name)]
public sealed partial class DynamicSecretIssueTests(DynamicSecretsFixture fixture)
{
    private readonly VaultFixture vault = fixture.Vault;

    [Fact]
    public async Task Issuing_creates_a_working_login_and_revoking_drops_it_with_the_password_in_no_row_or_log()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_issue");
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        using var issued = await DynamicSecretsApi.IssueAsync(admin, roleId);

        Assert.True(issued.Headers.CacheControl?.NoStore, "The response is not no-store");
        var credential = await DynamicSecretsApi.JsonAsync(issued);
        Assert.Equal(["expiresAtUtc", "leaseId", "password", "roleName", "username"], credential.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        var (username, password) = (credential.GetProperty("username").GetString()!, credential.GetProperty("password").GetString()!);
        Assert.Equal(1, await DynamicSecretsApi.SignInToTargetAsync(vault, username, password));
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        Assert.Equal((lease.Id, "it_dyn_issue", username), (credential.GetProperty("leaseId").GetGuid(), credential.GetProperty("roleName").GetString(), lease.CredentialIdentity));
        Assert.Equal(LeaseStatus.Active, lease.Status);

        using var revoked = await DynamicSecretsApi.RevokeAsync(admin, lease.Id);

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.True(revoked.Headers.CacheControl?.NoStore, "The response is not no-store");
        Assert.Equal("Revoked", (await DynamicSecretsApi.JsonAsync(revoked)).GetProperty("status").GetString());
        Assert.Empty(await DynamicSecretsApi.LoginsOfRoleAsync(vault, "it_dyn_issue"));
        Assert.Equal(LeaseStatus.Revoked, Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).Status);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal(new[] { AuditAction.DynamicCredentialIssued, AuditAction.DynamicCredentialRevoked },
            rows.Where(row => row.ResourceId == lease.Id).Select(row => row.Action));
        Assert.DoesNotContain(rows, row => $"{row.ResourceName} {row.Details}".Contains(password, StringComparison.Ordinal));
        Assert.DoesNotContain(vault.Log.Lines, line => line.Text.Contains(password, StringComparison.Ordinal));
        Assert.DoesNotContain(password, (await DynamicSecretsApi.GetAsync(admin)).GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_lists_the_roles_and_leases_in_the_contracts_shape_and_its_read_starts_no_evaluation_window()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_shape", defaultTtlMinutes: 20, maxTtlMinutes: 45);
        var holder = await vault.SignInNewAccountAsync("it-dyn-shape", IPAddress.Parse("10.172.0.1"));
        using var issued = await DynamicSecretsApi.IssueAsync(holder.Client, roleId);
        var (username, _) = await DynamicSecretsApi.IssuedCredentialAsync(issued);
        var windows = vault.Services.GetRequiredService<IEvaluationWindowStore>();
        var window = await windows.PeekAsync(LicenseFeatures.DynamicSecrets, CancellationToken.None);

        var page = await DynamicSecretsApi.GetAsync(holder.Client);
        var everyone = await DynamicSecretsApi.GetAsync(await vault.AdminClientAsync());

        Assert.Equal(["leaseScope", "leases", "licence", "roles"], page.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        var role = Assert.Single(page.GetProperty("roles").EnumerateArray(), role => role.GetProperty("roleId").GetGuid() == roleId);
        Assert.Equal(
            """{"roleId":"ROLE","name":"it_dyn_shape","description":"Integration test role.","project":"GLOBAL","environment":"Development","sensitivity":1,"sensitivityLabel":"Internal","backend":"SqlServer","defaultTtlMinutes":20,"maxTtlMinutes":45}""",
            role.GetRawText().Replace(roleId.ToString(), "ROLE", StringComparison.Ordinal));
        Assert.Equal("own", page.GetProperty("leaseScope").GetString());
        var lease = Assert.Single(page.GetProperty("leases").EnumerateArray());
        Assert.Equal(["closedAtUtc", "credentialIdentity", "expiresAtUtc", "holder", "issuedAtUtc", "leaseId", "revocationNote", "roleName", "status"],
            lease.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        Assert.Equal(("it_dyn_shape", username, "it-dyn-shape", "Active"), (lease.GetProperty("roleName").GetString(),
            lease.GetProperty("credentialIdentity").GetString(), lease.GetProperty("holder").GetString(), lease.GetProperty("status").GetString()));
        Assert.Equal(["evaluationDaysRemaining", "licensed"], page.GetProperty("licence").EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        Assert.Equal("everyone", everyone.GetProperty("leaseScope").GetString());
        Assert.Contains(everyone.GetProperty("leases").EnumerateArray(), row => row.GetProperty("credentialIdentity").GetString() == username);
        // Unchanged rather than absent: issuing above may already have started it.
        Assert.Equal(window, await windows.PeekAsync(LicenseFeatures.DynamicSecrets, CancellationToken.None));
        await DynamicSecretsApi.RevokeAsync(holder.Client, lease.GetProperty("leaseId").GetGuid());
    }

    // Confidential and higher roles open only from a trusted network, so these sessions come from the
    // private range the vault trusts by default, except the one that tests the network rule.
    [Theory]
    [InlineData("it_dyn_filter_project", SensitivityLevel.Internal, "IT-DYN-OTHER", ClearanceLevel.Standard, "10.172.0.10")]
    [InlineData("it_dyn_filter_clearance", SensitivityLevel.Secret, "GLOBAL", ClearanceLevel.Elevated, "10.172.0.11")]
    [InlineData("it_dyn_filter_network", SensitivityLevel.Confidential, "GLOBAL", ClearanceLevel.Elevated, "203.0.113.172")]
    public async Task A_role_the_caller_may_not_use_is_left_out_of_the_list_and_refused_on_issue_with_a_denial_row(
        string roleName, SensitivityLevel sensitivity, string project, ClearanceLevel clearance, string source)
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, roleName, sensitivity, project: project);
        var caller = await vault.SignInNewAccountAsync(roleName.Replace('_', '-'), IPAddress.Parse(source), clearance);
        var before = await AuditTrail.HeadAsync(vault);

        var page = await DynamicSecretsApi.GetAsync(caller.Client);
        using var refused = await DynamicSecretsApi.IssueAsync(caller.Client, roleId);

        Assert.DoesNotContain(page.GetProperty("roles").EnumerateArray(), role => role.GetProperty("roleId").GetGuid() == roleId);
        var problem = await DynamicSecretsApi.AssertProblemAsync(refused, 403, "access_denied");
        Assert.Equal($"Your clearance and project do not permit issuing '{roleName}'.", problem.GetProperty("title").GetString());
        var denial = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.SecretAccessDenied);
        Assert.Equal(("DynamicSecretRole", roleId, roleName), (denial.ResourceType, denial.ResourceId, denial.ResourceName));
        Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
    }

    // Secret roles open only from a trusted network, so these sessions come from the private range the
    // vault trusts by default, each from an address of its own so sign-in budgets stay apart.
    [Fact]
    public async Task A_stale_session_is_asked_for_proof_for_a_secret_role_without_a_denial_row_and_still_issues_an_internal_one()
    {
        var secretRole = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_stale_secret", SensitivityLevel.Secret);
        var internalRole = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_stale_internal");
        var account = await vault.SignInNewAccountAsync("it-dyn-stale", IPAddress.Parse("10.171.0.10"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(
            SessionApi.WithStrongAuthenticationAt(vault, account, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("10.171.0.10"));
        var before = await AuditTrail.HeadAsync(vault);

        using var refused = await DynamicSecretsApi.IssueAsync(stale.Client, secretRole);

        await AdministratorFreshnessTests.AssertStepUpAsync(refused, "code", "Confirm it's you before issuing 'it_dyn_stale_secret'.");
        Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, secretRole));
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.SecretAccessDenied);

        using var issued = await DynamicSecretsApi.IssueAsync(stale.Client, internalRole);

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, internalRole));
        await DynamicSecretsApi.RevokeAsync(stale.Client, lease.Id);
    }

    [Fact]
    public async Task A_refused_issue_records_the_denial_on_the_role_and_starts_no_evaluation_window()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_refused", SensitivityLevel.Secret);
        var standard = await vault.SignInNewAccountAsync("it-dyn-refused", IPAddress.Parse("10.171.0.20"));
        var topSecret = await vault.SignInNewAccountAsync("it-dyn-refused-stale", IPAddress.Parse("10.171.0.21"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(
            SessionApi.WithStrongAuthenticationAt(vault, topSecret, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("10.171.0.21"));
        var windows = vault.Services.GetRequiredService<IEvaluationWindowStore>();
        var window = await windows.PeekAsync(LicenseFeatures.DynamicSecrets, CancellationToken.None);
        var before = await AuditTrail.HeadAsync(vault);

        using var denied = await DynamicSecretsApi.IssueAsync(standard.Client, roleId);
        using var unproven = await DynamicSecretsApi.IssueAsync(stale.Client, roleId);

        await DynamicSecretsApi.AssertProblemAsync(denied, 403, "access_denied");
        await DynamicSecretsApi.AssertProblemAsync(unproven, 403, "step_up_required");
        var denial = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.SecretAccessDenied);
        Assert.Equal(("DynamicSecretRole", roleId, "it_dyn_refused", "it-dyn-refused"),
            (denial.ResourceType, denial.ResourceId, denial.ResourceName, denial.Username));
        Assert.False(string.IsNullOrWhiteSpace(denial.Details));
        Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        // Unchanged rather than absent: another test in this vault may already have started it.
        Assert.Equal(window, await windows.PeekAsync(LicenseFeatures.DynamicSecrets, CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_role_and_a_disabled_one_are_refused_with_their_own_codes()
    {
        var disabled = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_disabled", enabled: false);
        var admin = await vault.AdminClientAsync();

        using var unknown = await DynamicSecretsApi.IssueAsync(admin, Guid.NewGuid());
        using var off = await DynamicSecretsApi.IssueAsync(admin, disabled);

        Assert.Equal("That role no longer exists.",
            (await DynamicSecretsApi.AssertProblemAsync(unknown, 404, "role_not_found")).GetProperty("title").GetString());
        Assert.Equal("The role 'it_dyn_disabled' is disabled and cannot issue credentials.",
            (await DynamicSecretsApi.AssertProblemAsync(off, 409, "role_disabled")).GetProperty("title").GetString());
        Assert.DoesNotContain((await DynamicSecretsApi.GetAsync(admin)).GetProperty("roles").EnumerateArray(),
            role => role.GetProperty("roleId").GetGuid() == disabled);
        Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, disabled));
    }

    [Fact]
    public async Task A_write_without_the_antiforgery_token_is_refused_and_issues_nothing()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_antiforgery");
        var admin = await vault.AdminClientAsync();

        using var issue = await admin.PostAsync($"{DynamicSecretsApi.Path}/issue", System.Net.Http.Json.JsonContent.Create(new { roleId }));
        using var revoke = await admin.PostAsync($"{DynamicSecretsApi.Path}/leases/{Guid.NewGuid()}/revoke", null);

        await DynamicSecretsApi.AssertProblemAsync(issue, 400, "antiforgery_rejected");
        await DynamicSecretsApi.AssertProblemAsync(revoke, 400, "antiforgery_rejected");
        Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
    }

    [Fact]
    public async Task Only_the_holder_or_an_administrator_revokes_and_every_other_lease_is_the_same_not_found()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_revoke_matrix");
        var holder = await vault.SignInNewAccountAsync("it-dyn-revoke-holder", IPAddress.Parse("10.172.0.20"));
        var other = await vault.SignInNewAccountAsync("it-dyn-revoke-other", IPAddress.Parse("10.172.0.21"));
        var admin = await vault.AdminClientAsync();
        (await DynamicSecretsApi.IssueAsync(holder.Client, roleId)).EnsureSuccessStatusCode();
        (await DynamicSecretsApi.IssueAsync(holder.Client, roleId)).EnsureSuccessStatusCode();
        var leases = (await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).OrderBy(lease => lease.IssuedAtUtc).ToArray();

        using var byOther = await DynamicSecretsApi.RevokeAsync(other.Client, leases[0].Id);
        using var unknown = await DynamicSecretsApi.RevokeAsync(holder.Client, Guid.NewGuid());
        using var byHolder = await DynamicSecretsApi.RevokeAsync(holder.Client, leases[0].Id);
        using var closed = await DynamicSecretsApi.RevokeAsync(holder.Client, leases[0].Id);
        using var byAdministrator = await DynamicSecretsApi.RevokeAsync(admin, leases[1].Id);

        const string notYours = "That lease is not active, or is not yours to revoke.";
        Assert.Equal(notYours, (await DynamicSecretsApi.AssertProblemAsync(byOther, 404, "lease_not_found")).GetProperty("title").GetString());
        Assert.Equal(notYours, (await DynamicSecretsApi.AssertProblemAsync(unknown, 404, "lease_not_found")).GetProperty("title").GetString());
        Assert.Equal(notYours, (await DynamicSecretsApi.AssertProblemAsync(closed, 404, "lease_not_found")).GetProperty("title").GetString());
        Assert.Equal("""{"status":"Revoked","revocationNote":null}""", (await DynamicSecretsApi.JsonAsync(byHolder)).GetRawText());
        Assert.Equal("""{"status":"Revoked","revocationNote":null}""", (await DynamicSecretsApi.JsonAsync(byAdministrator)).GetRawText());
        Assert.Empty(await DynamicSecretsApi.LoginsOfRoleAsync(vault, "it_dyn_revoke_matrix"));
    }

    [Fact]
    public async Task A_revoke_the_backend_refuses_answers_its_fixed_note_and_lists_it_on_the_lease()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_revoke_refused", revocation: "THROW 51000, 'Revocation refused.', 1;");
        var admin = await vault.AdminClientAsync();
        var (username, _) = await DynamicSecretsApi.IssuedCredentialAsync(await DynamicSecretsApi.IssueAsync(admin, roleId));
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        try
        {
            using var revoked = await DynamicSecretsApi.RevokeAsync(admin, lease.Id);

            Assert.Equal("""{"status":"RevocationFailed","revocationNote":"The backend refused to destroy the credential; it may still work."}""",
                (await DynamicSecretsApi.JsonAsync(revoked)).GetRawText());
            var listed = Assert.Single((await DynamicSecretsApi.GetAsync(admin)).GetProperty("leases").EnumerateArray(),
                row => row.GetProperty("leaseId").GetGuid() == lease.Id);
            Assert.Equal(("RevocationFailed", "The backend refused to destroy the credential; it may still work."),
                (listed.GetProperty("status").GetString(), listed.GetProperty("revocationNote").GetString()));
            Assert.DoesNotContain(vault.Log.Lines, line => line.Text.Contains("Revocation refused.", StringComparison.Ordinal));
        }
        finally
        {
            await DynamicSecretsApi.DropLoginAsync(vault, username);
        }
    }

    [Fact]
    public async Task A_recipe_whose_create_user_fails_leaves_no_login_and_issues_nothing()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_user_fails", creation: """
            CREATE LOGIN [{{name}}] WITH PASSWORD = '{{password}}';
            CREATE USER [{{name}}] FOR LOGIN [it_no_such_login];
            """);
        var admin = await vault.AdminClientAsync();

        using var refused = await DynamicSecretsApi.IssueAsync(admin, roleId);

        Assert.Equal("The backend refused to mint a credential for 'it_dyn_user_fails'. Nothing was issued.",
            (await DynamicSecretsApi.AssertProblemAsync(refused, 502, "mint_refused")).GetProperty("title").GetString());
        Assert.Empty(await DynamicSecretsApi.LoginsOfRoleAsync(vault, "it_dyn_user_fails"));
        Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
    }

    [Fact]
    public async Task A_mint_whose_cleanup_also_fails_says_so_and_logs_the_login_at_critical_with_no_password_anywhere()
    {
        // SQL Server's error for this CREATE USER quotes the login it names, which here is the password.
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_incomplete",
            creation: """
                CREATE LOGIN [{{name}}] WITH PASSWORD = '{{password}}';
                CREATE USER [{{name}}] FOR LOGIN [{{password}}];
                """,
            revocation: "THROW 51000, 'Revocation refused.', 1;");
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        using var incomplete = await DynamicSecretsApi.IssueAsync(admin, roleId);

        var problem = await DynamicSecretsApi.AssertProblemAsync(incomplete, 502, "mint_incomplete");
        Assert.Equal(
            "The backend failed part-way through, and the vault could not confirm that the login it started to create was removed. " +
            "No password was shown or kept. An administrator must check the target.",
            problem.GetProperty("title").GetString());
        var login = Assert.Single(await DynamicSecretsApi.LoginsOfRoleAsync(vault, "it_dyn_incomplete"));
        try
        {
            var alert = Assert.Single(vault.Log.Lines, line => line.Level == LogEventLevel.Fatal && line.Text.Contains(login, StringComparison.Ordinal));
            Assert.Contains("it_dyn_incomplete", alert.Text);
            Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
            Assert.DoesNotMatch(MintedPassword(), problem.GetRawText());
            Assert.DoesNotContain(vault.Log.Lines, line => line.Text.Contains("is not a valid login", StringComparison.Ordinal));
            Assert.DoesNotContain(vault.Log.Lines, line => MintedPassword().IsMatch(line.Text));
            Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => MintedPassword().IsMatch($"{row.ResourceName} {row.Details}"));
        }
        finally
        {
            await DynamicSecretsApi.DropLoginAsync(vault, login);
        }
    }

    [Theory]
    [InlineData("it_dyn_ttl_long_default", 2880, 2880, null, 1440)]
    [InlineData("it_dyn_ttl_short_default", 30, 2880, null, 30)]
    [InlineData("it_dyn_ttl_a_day", 30, 2880, 1440, 1440)]
    [InlineData("it_dyn_ttl_above_role", 15, 60, 90, 60)]
    public async Task A_lease_issued_now_lasts_at_most_a_day(string roleName, int defaultTtl, int maxTtl, int? requested, int expectedMinutes)
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, roleName, defaultTtlMinutes: defaultTtl, maxTtlMinutes: maxTtl);
        var admin = await vault.AdminClientAsync();

        using var issued = await DynamicSecretsApi.IssueAsync(admin, roleId, requested);

        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), lease.ExpiresAtUtc - lease.IssuedAtUtc);
        // The page shows the expiry to the second. PostgreSQL keeps whole microseconds, and a Linux clock reads 100 ns.
        Assert.Equal(lease.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            (await DynamicSecretsApi.JsonAsync(issued)).GetProperty("expiresAtUtc").GetDateTimeOffset().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        await DynamicSecretsApi.RevokeAsync(admin, lease.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task A_lease_length_outside_a_day_is_refused_as_invalid(int requested)
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, $"it_dyn_ttl_invalid_{requested}", defaultTtlMinutes: 30, maxTtlMinutes: 2880);
        var admin = await vault.AdminClientAsync();

        using var refused = await DynamicSecretsApi.IssueAsync(admin, roleId, requested);

        var problem = await DynamicSecretsApi.AssertProblemAsync(refused, 400, "validation_failed");
        Assert.Equal("Enter a lease length between 1 and 1440 minutes.", problem.GetProperty("title").GetString());
        Assert.Equal("""{"ttlMinutes":["Enter a lease length between 1 and 1440 minutes."]}""", problem.GetProperty("errors").GetRawText());
        Assert.Empty(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
    }

    [Fact]
    public async Task A_two_day_lease_stored_before_the_ceiling_stays_active_past_a_day_until_it_expires()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_stored", defaultTtlMinutes: 2880, maxTtlMinutes: 2880);
        // Whole microseconds, as PostgreSQL stores it, so the expiry read back is the one written.
        var issuedAt = TimeProvider.System.GetAuditUtcNow().AddMinutes(-1500);
        var lease = new DynamicSecretLease
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            RoleName = "it_dyn_stored",
            UserId = Guid.NewGuid(),
            Username = "it-dyn-stored",
            CredentialIdentity = "ev_it_dyn_stored_k3mq8x2ptw",
            IssuedAtUtc = issuedAt,
            ExpiresAtUtc = issuedAt.AddMinutes(2880),
            Status = LeaseStatus.Active,
        };
        using (var scope = vault.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            db.DynamicSecretLeases.Add(lease);
            await db.SaveChangesAsync();
        }

        await DynamicSecretsApi.SweepAsync(vault);

        var stored = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        Assert.Equal((LeaseStatus.Active, issuedAt.AddMinutes(2880)), (stored.Status, stored.ExpiresAtUtc));

        await DynamicSecretsApi.ExpireAtAsync(vault, lease.Id, DateTimeOffset.UtcNow.AddSeconds(-1));
        await DynamicSecretsApi.SweepAsync(vault);

        Assert.Equal(LeaseStatus.Expired, Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).Status);
    }

    [Fact]
    public async Task A_lease_closed_with_a_backends_own_error_text_lists_only_the_fixed_note()
    {
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_raw_note");
        var admin = await vault.AdminClientAsync();
        var lease = new DynamicSecretLease
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            RoleName = "it_dyn_raw_note",
            UserId = Guid.NewGuid(),
            Username = "it-dyn-raw-note",
            CredentialIdentity = "ev_it_dyn_raw_note_q2wn7v4xks",
            // Issued now, so it stays among an administrator's 50 newest; a failed lease is never swept.
            IssuedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1),
            Status = LeaseStatus.Active,
        };
        lease.Close(LeaseStatus.RevocationFailed, DateTimeOffset.UtcNow,
            "Cannot drop the login 'ev_it_dyn_raw_note_q2wn7v4xks' on sql-prod-07.internal.");
        using (var scope = vault.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            db.DynamicSecretLeases.Add(lease);
            await db.SaveChangesAsync();
        }

        var page = await DynamicSecretsApi.GetAsync(admin);

        var listed = Assert.Single(page.GetProperty("leases").EnumerateArray(), row => row.GetProperty("leaseId").GetGuid() == lease.Id);
        Assert.Equal("The backend refused to destroy the credential; it may still work.", listed.GetProperty("revocationNote").GetString());
        Assert.DoesNotContain("sql-prod-07", page.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>A minted password's shape: 40 letters and digits, standing alone.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z0-9]{40}(?![A-Za-z0-9])")]
    private static partial Regex MintedPassword();
}

/// <summary>Production roles open only inside the production window, so outside it the list leaves them out and issuing refuses.</summary>
[Collection(ClosedProductionWindowCollection.Name)]
public sealed class DynamicSecretsProductionWindowTests(ClosedProductionWindowFixture fixture)
{
    [Fact]
    public async Task A_production_role_is_left_out_and_refused_with_a_denial_row_while_the_window_is_closed()
    {
        var vault = fixture.Vault;
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, "it_dyn_window", environment: SecretEnvironment.Production);
        var member = await vault.SignInNewAccountAsync("it-dyn-window", IPAddress.Parse("10.172.0.30"));
        var before = await AuditTrail.HeadAsync(vault);

        var page = await DynamicSecretsApi.GetAsync(member.Client);
        using var refused = await DynamicSecretsApi.IssueAsync(member.Client, roleId);

        Assert.DoesNotContain(page.GetProperty("roles").EnumerateArray(), role => role.GetProperty("roleId").GetGuid() == roleId);
        await DynamicSecretsApi.AssertProblemAsync(refused, 403, "access_denied");
        Assert.Contains(await AuditTrail.RowsAfterAsync(vault, before),
            row => row.Action == AuditAction.SecretAccessDenied && row.ResourceId == roleId);
    }
}
