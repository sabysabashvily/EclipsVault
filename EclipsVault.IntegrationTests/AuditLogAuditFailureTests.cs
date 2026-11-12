using System.Net;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Opening, verifying and checkpointing the audit trail happen only with their audit row. Each test
/// signs in with auditing working, then rejects audit writes, so the audited action is the step that fails.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class AuditLogAuditFailureTests(AuditFailureFixture failure)
{
    private const string AuditUnavailablePage = "/Home/Error?code=503";

    [Fact]
    public async Task Opening_the_audit_log_when_it_cannot_be_recorded_shows_no_entries()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await admin.GetAsync("/Audit");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_verification_that_cannot_be_recorded_shows_no_result()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var token = await LegacyAccessRequests.TokenAsync(admin);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyAuditPage.PostAsync(admin, "/Audit/Verify", token);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_checkpoint_that_cannot_be_recorded_is_not_created()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var token = await LegacyAccessRequests.TokenAsync(admin);
        var checkpoints = await CheckpointCountAsync(vault);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacyAuditPage.PostAsync(admin, "/Audit/CreateCheckpoint", token);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(AuditUnavailablePage, response.Headers.Location?.OriginalString);
            Assert.Equal(checkpoints, await CheckpointCountAsync(vault));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    private static async Task<int> CheckpointCountAsync(VaultFixture vault)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AuditCheckpoints.CountAsync();
    }
}
