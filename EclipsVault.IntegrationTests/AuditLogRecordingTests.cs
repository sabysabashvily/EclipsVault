using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Access to the audit trail is itself recorded. Each test acts as an administrator of its own, so it
/// asserts only on the rows that administrator wrote.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class AuditLogRecordingTests(VaultFixture vault)
{
    [Fact]
    public async Task Each_opening_of_the_audit_log_writes_one_view_row()
    {
        const string viewer = "it-audit-viewer";
        var session = await vault.SignInNewAccountAsync(viewer, IPAddress.Parse("203.0.113.160"), ClearanceLevel.TopSecret);
        var before = await AuditTrail.HeadAsync(vault);

        var first = await session.Client.GetAsync("/Audit");
        var second = await session.Client.GetAsync("/Audit");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("Audit log viewed", await second.Content.ReadAsStringAsync());
        Assert.Equal(
            [AuditAction.AuditLogViewed, AuditAction.AuditLogViewed],
            (await AuditTrail.RowsAfterAsync(vault, before)).Where(row => row.Username == viewer).Select(row => row.Action));
    }

    [Fact]
    public async Task Verifying_the_chain_writes_one_row_with_its_outcome()
    {
        const string verifier = "it-audit-verifier";
        var session = await vault.SignInNewAccountAsync(verifier, IPAddress.Parse("203.0.113.161"), ClearanceLevel.TopSecret);
        var token = await LegacyAccessRequests.TokenAsync(session.Client);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAuditPage.PostAsync(session.Client, "/Audit/Verify", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("✓ Hash chain intact", page);
        Assert.Contains("Audit chain verified", page);
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.Username == verifier);
        Assert.Equal(AuditAction.AuditChainVerified, row.Action);
        Assert.False(row.IsCritical);
        Assert.Contains(" entries verified. ", row.Details);
    }

    [Fact]
    public async Task A_checkpoint_is_recorded_with_the_checkpoint_it_created()
    {
        const string signer = "it-audit-signer";
        var session = await vault.SignInNewAccountAsync(signer, IPAddress.Parse("203.0.113.162"), ClearanceLevel.TopSecret);
        var token = await LegacyAccessRequests.TokenAsync(session.Client);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyAuditPage.PostAsync(session.Client, "/Audit/CreateCheckpoint", token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Audit", response.Headers.Location?.OriginalString);
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.Username == signer);
        Assert.Equal(AuditAction.AuditCheckpointCreated, row.Action);
        using var scope = vault.Services.CreateScope();
        var checkpoint = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>()
            .AuditCheckpoints.SingleAsync(checkpoint => checkpoint.Id == row.ResourceId);
        Assert.Equal($"Signed the audit chain at sequence {checkpoint.Sequence} with key {checkpoint.SigningKeyId}", row.Details);
    }
}
