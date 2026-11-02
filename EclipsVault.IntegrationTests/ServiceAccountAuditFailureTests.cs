using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A change to a service account or one of its keys happens only with its audit row. Each test sets up
/// with auditing working, then rejects audit writes, so the change is the step that fails.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class ServiceAccountAuditFailureTests(AuditFailureFixture failure)
{
    private const string AuditUnavailablePage = "/Home/Error?code=503";

    [Fact]
    public async Task A_create_that_cannot_be_recorded_leaves_no_service_account()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyServiceAccounts.CreateAsync(admin, "it-unrecorded-sa-create");

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Null(await LegacyServiceAccounts.FindAsync(vault, "it-unrecorded-sa-create"));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Enabling_that_cannot_be_recorded_leaves_the_service_account_disabled()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var account = await LegacyServiceAccounts.CreatedAsync(vault, admin, "it-unrecorded-sa-enable");
        await LegacyServiceAccounts.SetEnabledAsync(admin, account.Id, enabled: false);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyServiceAccounts.SetEnabledAsync(admin, account.Id, enabled: true);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.True((await LegacyServiceAccounts.FindAsync(vault, "it-unrecorded-sa-enable"))!.IsDisabled);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task Disabling_that_cannot_be_recorded_leaves_the_service_account_enabled()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var account = await LegacyServiceAccounts.CreatedAsync(vault, admin, "it-unrecorded-sa-disable");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyServiceAccounts.SetEnabledAsync(admin, account.Id, enabled: false);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.False((await LegacyServiceAccounts.FindAsync(vault, "it-unrecorded-sa-disable"))!.IsDisabled);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_delete_that_cannot_be_recorded_leaves_the_service_account_and_its_keys()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var account = await LegacyServiceAccounts.CreatedAsync(vault, admin, "it-unrecorded-sa-delete");
        await LegacyServiceAccounts.IssueKeyAsync(admin, account.Id);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyServiceAccounts.DeleteAsync(admin, account.Id);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            var stored = await LegacyServiceAccounts.FindAsync(vault, "it-unrecorded-sa-delete");
            Assert.Single(stored!.Keys);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_key_issue_that_cannot_be_recorded_leaves_no_key()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var account = await LegacyServiceAccounts.CreatedAsync(vault, admin, "it-unrecorded-key-issue");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyServiceAccounts.IssueKeyAsync(admin, account.Id);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Empty((await LegacyServiceAccounts.FindAsync(vault, "it-unrecorded-key-issue"))!.Keys);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_key_revoke_that_cannot_be_recorded_leaves_the_key_active()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var account = await LegacyServiceAccounts.CreatedAsync(vault, admin, "it-unrecorded-key-revoke");
        await LegacyServiceAccounts.IssueKeyAsync(admin, account.Id);
        var key = Assert.Single((await LegacyServiceAccounts.FindAsync(vault, "it-unrecorded-key-revoke"))!.Keys);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyServiceAccounts.RevokeKeyAsync(admin, account.Id, key.Id);

            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Null(Assert.Single((await LegacyServiceAccounts.FindAsync(vault, "it-unrecorded-key-revoke"))!.Keys).RevokedAtUtc);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }
}
