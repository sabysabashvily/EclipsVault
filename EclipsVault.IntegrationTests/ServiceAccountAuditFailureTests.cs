using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A change to a service account or one of its keys happens only with its audit row. Each test sets up
/// with auditing working, then rejects audit writes, so the change is the step that fails.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class ServiceAccountAuditFailureTests(AuditFailureFixture failure)
{
    [Fact]
    public async Task A_create_that_cannot_be_recorded_leaves_no_service_account()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await ServiceAccountsApi.CreateAsync(admin, "it-unrecorded-sa-create");

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.Null(await ServiceAccountsApi.FindAsync(vault, "it-unrecorded-sa-create"));
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
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin, "it-unrecorded-sa-enable");
        await ServiceAccountsApi.SetEnabledAsync(admin, account.Id, enabled: false);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await ServiceAccountsApi.SetEnabledAsync(admin, account.Id, enabled: true);

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.True((await ServiceAccountsApi.FindAsync(vault, "it-unrecorded-sa-enable"))!.IsDisabled);
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
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin, "it-unrecorded-sa-disable");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await ServiceAccountsApi.SetEnabledAsync(admin, account.Id, enabled: false);

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.False((await ServiceAccountsApi.FindAsync(vault, "it-unrecorded-sa-disable"))!.IsDisabled);
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
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin, "it-unrecorded-sa-delete");
        await ServiceAccountsApi.IssueKeyAsync(admin, account.Id);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await ServiceAccountsApi.DeleteAsync(admin, account.Id);

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            var stored = await ServiceAccountsApi.FindAsync(vault, "it-unrecorded-sa-delete");
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
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin, "it-unrecorded-key-issue");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await ServiceAccountsApi.IssueKeyAsync(admin, account.Id);

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.Empty((await ServiceAccountsApi.FindAsync(vault, "it-unrecorded-key-issue"))!.Keys);
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
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin, "it-unrecorded-key-revoke");
        await ServiceAccountsApi.IssueKeyAsync(admin, account.Id);
        var key = Assert.Single((await ServiceAccountsApi.FindAsync(vault, "it-unrecorded-key-revoke"))!.Keys);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await ServiceAccountsApi.RevokeKeyAsync(admin, account.Id, key.Id);

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.Null(Assert.Single((await ServiceAccountsApi.FindAsync(vault, "it-unrecorded-key-revoke"))!.Keys).RevokedAtUtc);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }
}
