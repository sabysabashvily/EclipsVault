using System.Net;
using System.Text.RegularExpressions;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Issuing and revoking dynamic credentials through the Legacy page, against real SQL Server logins on
/// the vault's own target database.
/// </summary>
[Collection(DynamicSecretsCollection.Name)]
public sealed partial class DynamicSecretIssueTests(DynamicSecretsFixture fixture)
{
    private readonly VaultFixture vault = fixture.Vault;

    [Fact]
    public async Task Issuing_creates_a_working_login_and_revoking_drops_it_with_the_password_in_no_row_or_log()
    {
        var roleId = await LegacyDynamicSecrets.AddRoleAsync(vault, "it_dyn_issue");
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        var issued = await LegacyDynamicSecrets.IssueAsync(admin, roleId);

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var (username, password) = await LegacyDynamicSecrets.IssuedCredentialAsync(issued);
        Assert.Equal(1, await LegacyDynamicSecrets.SignInToTargetAsync(vault, username, password));
        var lease = Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId));
        Assert.Equal(username, lease.CredentialIdentity);
        Assert.Equal(LeaseStatus.Active, lease.Status);

        var revoked = await LegacyDynamicSecrets.RevokeAsync(admin, lease.Id);

        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);
        Assert.Equal("Credential handed back — it no longer works.", await LegacyDynamicSecrets.FlashAsync(admin));
        Assert.Empty(await LegacyDynamicSecrets.LoginsOfRoleAsync(vault, "it_dyn_issue"));
        Assert.Equal(LeaseStatus.Revoked, Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId)).Status);
        var rows = await AuditTrail.RowsAfterAsync(vault, before);
        Assert.Equal(new[] { AuditAction.DynamicCredentialIssued, AuditAction.DynamicCredentialRevoked },
            rows.Where(row => row.ResourceId == lease.Id).Select(row => row.Action));
        Assert.DoesNotContain(rows, row => $"{row.ResourceName} {row.Details}".Contains(password, StringComparison.Ordinal));
        Assert.DoesNotContain(vault.Log.Lines, line => line.Text.Contains(password, StringComparison.Ordinal));
    }

    // Secret roles open only from a trusted network, so these sessions come from the private range the
    // vault trusts by default, each from an address of its own so sign-in budgets stay apart.
    [Fact]
    public async Task A_stale_session_is_refused_a_secret_role_without_a_denial_row_and_still_issues_an_internal_one()
    {
        var secretRole = await LegacyDynamicSecrets.AddRoleAsync(vault, "it_dyn_stale_secret", SensitivityLevel.Secret);
        var internalRole = await LegacyDynamicSecrets.AddRoleAsync(vault, "it_dyn_stale_internal");
        var account = await vault.SignInNewAccountAsync("it-dyn-stale", IPAddress.Parse("10.171.0.10"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(
            SessionApi.WithStrongAuthenticationAt(vault, account, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("10.171.0.10"));
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyDynamicSecrets.IssueAsync(stale.Client, secretRole);

        Assert.Equal("Confirm it's you before issuing 'it_dyn_stale_secret': sign in again, then retry.",
            await LegacyDynamicSecrets.FlashAsync(stale.Client));
        Assert.Empty(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, secretRole));
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.SecretAccessDenied);

        var issued = await LegacyDynamicSecrets.IssueAsync(stale.Client, internalRole);

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var lease = Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, internalRole));
        await LegacyDynamicSecrets.RevokeAsync(stale.Client, lease.Id);
    }

    [Fact]
    public async Task A_refused_issue_records_the_denial_on_the_role_and_starts_no_evaluation_window()
    {
        var roleId = await LegacyDynamicSecrets.AddRoleAsync(vault, "it_dyn_refused", SensitivityLevel.Secret);
        var standard = await vault.SignInNewAccountAsync("it-dyn-refused", IPAddress.Parse("10.171.0.20"));
        var topSecret = await vault.SignInNewAccountAsync("it-dyn-refused-stale", IPAddress.Parse("10.171.0.21"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(
            SessionApi.WithStrongAuthenticationAt(vault, topSecret, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("10.171.0.21"));
        var windows = vault.Services.GetRequiredService<IEvaluationWindowStore>();
        var window = await windows.PeekAsync(LicenseFeatures.DynamicSecrets, CancellationToken.None);
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyDynamicSecrets.IssueAsync(standard.Client, roleId);
        await LegacyDynamicSecrets.IssueAsync(stale.Client, roleId);

        Assert.Equal("Your clearance and project do not permit issuing 'it_dyn_refused'.", await LegacyDynamicSecrets.FlashAsync(standard.Client));
        var denial = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.SecretAccessDenied);
        Assert.Equal(("DynamicSecretRole", roleId, "it_dyn_refused", "it-dyn-refused"),
            (denial.ResourceType, denial.ResourceId, denial.ResourceName, denial.Username));
        Assert.False(string.IsNullOrWhiteSpace(denial.Details));
        Assert.Empty(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId));
        // Unchanged rather than absent: another test in this vault may already have started it.
        Assert.Equal(window, await windows.PeekAsync(LicenseFeatures.DynamicSecrets, CancellationToken.None));
    }

    [Fact]
    public async Task A_recipe_whose_create_user_fails_leaves_no_login_and_issues_nothing()
    {
        var roleId = await LegacyDynamicSecrets.AddRoleAsync(vault, "it_dyn_user_fails", creation: """
            CREATE LOGIN [{{name}}] WITH PASSWORD = '{{password}}';
            CREATE USER [{{name}}] FOR LOGIN [it_no_such_login];
            """);
        var admin = await vault.AdminClientAsync();

        await LegacyDynamicSecrets.IssueAsync(admin, roleId);

        Assert.Equal("The backend refused to mint a credential for 'it_dyn_user_fails'. Nothing was issued.",
            await LegacyDynamicSecrets.FlashAsync(admin));
        Assert.Empty(await LegacyDynamicSecrets.LoginsOfRoleAsync(vault, "it_dyn_user_fails"));
        Assert.Empty(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId));
    }

    [Fact]
    public async Task A_mint_whose_cleanup_also_fails_says_so_and_logs_the_login_at_critical_with_no_password_anywhere()
    {
        // SQL Server's error for this CREATE USER quotes the login it names, which here is the password.
        var roleId = await LegacyDynamicSecrets.AddRoleAsync(vault, "it_dyn_incomplete",
            creation: """
                CREATE LOGIN [{{name}}] WITH PASSWORD = '{{password}}';
                CREATE USER [{{name}}] FOR LOGIN [{{password}}];
                """,
            revocation: "THROW 51000, 'Revocation refused.', 1;");
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyDynamicSecrets.IssueAsync(admin, roleId);

        Assert.Equal(
            "The backend failed part-way through, and the vault could not confirm that the login it started to create was removed. " +
            "No password was shown or kept. An administrator must check the target.",
            await LegacyDynamicSecrets.FlashAsync(admin));
        var login = Assert.Single(await LegacyDynamicSecrets.LoginsOfRoleAsync(vault, "it_dyn_incomplete"));
        try
        {
            var alert = Assert.Single(vault.Log.Lines, line => line.Level == LogEventLevel.Fatal && line.Text.Contains(login, StringComparison.Ordinal));
            Assert.Contains("it_dyn_incomplete", alert.Text);
            Assert.Empty(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId));
            Assert.DoesNotContain(vault.Log.Lines, line => line.Text.Contains("is not a valid login", StringComparison.Ordinal));
            Assert.DoesNotContain(vault.Log.Lines, line => MintedPassword().IsMatch(line.Text));
            Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => MintedPassword().IsMatch($"{row.ResourceName} {row.Details}"));
        }
        finally
        {
            await LegacyDynamicSecrets.DropLoginAsync(vault, login);
        }
    }

    [Theory]
    [InlineData("it_dyn_ttl_long_default", 2880, 2880, null, 1440)]
    [InlineData("it_dyn_ttl_short_default", 30, 2880, null, 30)]
    [InlineData("it_dyn_ttl_a_day", 30, 2880, 1440, 1440)]
    public async Task A_lease_issued_now_lasts_at_most_a_day(string roleName, int defaultTtl, int maxTtl, int? requested, int expectedMinutes)
    {
        var roleId = await LegacyDynamicSecrets.AddRoleAsync(vault, roleName, defaultTtlMinutes: defaultTtl, maxTtlMinutes: maxTtl);
        var admin = await vault.AdminClientAsync();

        await LegacyDynamicSecrets.IssueAsync(admin, roleId, requested);

        var lease = Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId));
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), lease.ExpiresAtUtc - lease.IssuedAtUtc);
        await LegacyDynamicSecrets.RevokeAsync(admin, lease.Id);
    }

    [Fact]
    public async Task A_lease_longer_than_a_day_is_refused_as_invalid()
    {
        var roleId = await LegacyDynamicSecrets.AddRoleAsync(vault, "it_dyn_ttl_too_long", defaultTtlMinutes: 30, maxTtlMinutes: 2880);
        var admin = await vault.AdminClientAsync();

        await LegacyDynamicSecrets.IssueAsync(admin, roleId, 1441);

        Assert.Equal("Enter a lease length between 1 and 1440 minutes.", await LegacyDynamicSecrets.FlashAsync(admin));
        Assert.Empty(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId));
    }

    [Fact]
    public async Task A_two_day_lease_stored_before_the_ceiling_stays_active_past_a_day_until_it_expires()
    {
        var roleId = await LegacyDynamicSecrets.AddRoleAsync(vault, "it_dyn_stored", defaultTtlMinutes: 2880, maxTtlMinutes: 2880);
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-1500);
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

        await LegacyDynamicSecrets.SweepAsync(vault);

        var stored = Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId));
        Assert.Equal((LeaseStatus.Active, issuedAt.AddMinutes(2880)), (stored.Status, stored.ExpiresAtUtc));

        await LegacyDynamicSecrets.ExpireAtAsync(vault, lease.Id, DateTimeOffset.UtcNow.AddSeconds(-1));
        await LegacyDynamicSecrets.SweepAsync(vault);

        Assert.Equal(LeaseStatus.Expired, Assert.Single(await LegacyDynamicSecrets.LeasesOfRoleAsync(vault, roleId)).Status);
    }

    /// <summary>A minted password's shape: 40 letters and digits, standing alone.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z0-9]{40}(?![A-Za-z0-9])")]
    private static partial Regex MintedPassword();
}
