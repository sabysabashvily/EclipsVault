using System.Net;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// An administrator's change to an account happens only with its audit row. Each test sets up with
/// auditing working, then rejects audit writes, so the change is the step that fails.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class AdministratorAuditFailureTests(AuditFailureFixture failure)
{
    private const string AuditUnavailablePage = "/Home/Error?code=503";

    [Fact]
    public async Task A_create_that_cannot_be_recorded_leaves_no_account()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyUsers.CreateAsync(admin, "it-unrecorded-create");

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Null(await FindAccountAsync("it-unrecorded-create"));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_role_change_that_cannot_be_recorded_leaves_the_role_and_its_sessions()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var account = await CreateAccountAsync(admin, "it-unrecorded-role");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyUsers.EditRoleAsync(admin, account.Id, ClearanceLevel.Secret, "OTHER");

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            var stored = await FindAccountAsync("it-unrecorded-role");
            Assert.Equal(ClearanceLevel.Standard, stored!.Clearance);
            Assert.Equal("GLOBAL", stored.ProjectKey);
            Assert.Equal(account.CredentialGeneration, stored.CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Enabling_that_cannot_be_recorded_leaves_the_account_disabled()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var account = await CreateAccountAsync(admin, "it-unrecorded-enable");
        await LegacyUsers.SetEnabledAsync(admin, account.Id, enabled: false);
        var disabled = await FindAccountAsync("it-unrecorded-enable");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyUsers.SetEnabledAsync(admin, account.Id, enabled: true);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            var stored = await FindAccountAsync("it-unrecorded-enable");
            Assert.True(stored!.IsDisabled);
            Assert.Equal(disabled!.CredentialGeneration, stored.CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Disabling_that_cannot_be_recorded_leaves_the_account_enabled_with_its_generation()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var account = await CreateAccountAsync(admin, "it-unrecorded-disable");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyUsers.SetEnabledAsync(admin, account.Id, enabled: false);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            var stored = await FindAccountAsync("it-unrecorded-disable");
            Assert.False(stored!.IsDisabled);
            Assert.Equal(account.CredentialGeneration, stored.CredentialGeneration);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task An_unlock_that_cannot_be_recorded_leaves_the_account_locked()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var account = await CreateAccountAsync(admin, "it-unrecorded-unlock");
        var lockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
        using (var setup = vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var stored = await db.Users.SingleAsync(user => user.Id == account.Id);
            stored.FailedAccessCount = 2;
            stored.LockedUntilUtc = lockedUntil;
            await db.SaveChangesAsync();
        }
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyUsers.UnlockAsync(admin, account.Id);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            var stored = await FindAccountAsync("it-unrecorded-unlock");
            Assert.Equal(2, stored!.FailedAccessCount);
            Assert.NotNull(stored.LockedUntilUtc);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_delete_that_cannot_be_recorded_leaves_the_account()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var account = await CreateAccountAsync(admin, "it-unrecorded-delete");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyUsers.DeleteAsync(admin, account.Id);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Equal(account.Id, (await FindAccountAsync("it-unrecorded-delete"))?.Id);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_force_logout_that_cannot_be_recorded_still_signs_the_account_out_and_says_so()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var owner = await vault.SignInNewAccountAsync("it-unrecorded-logout", IPAddress.Parse("198.51.100.160"));
        var account = await FindAccountAsync("it-unrecorded-logout");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyUsers.ForceLogoutAsync(admin, account!.Id);

            Assert.Equal(LegacyUsers.UsersPage, response.Headers.Location?.OriginalString);
            Assert.Contains("All of the user's sessions were revoked, but the vault couldn't record it in the audit trail.",
                WebUtility.HtmlDecode(await admin.GetStringAsync(LegacyUsers.UsersPage)), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    private async Task<User> CreateAccountAsync(HttpClient admin, string username)
    {
        Assert.Equal(LegacyUsers.UsersPage, (await LegacyUsers.CreateAsync(admin, username)).Headers.Location?.OriginalString);
        return (await FindAccountAsync(username))!;
    }

    private async Task<User?> FindAccountAsync(string username)
    {
        using var scope = failure.Vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Username == username);
    }
}
