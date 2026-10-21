using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class ConcurrentShareRevocationTests(VaultFixture vault)
{
    [Fact]
    public async Task Overlapping_revocations_report_one_success_and_one_missing_share_with_one_audit_row()
    {
        var issuer = await vault.SignInNewAccountAsync("it-shares-race-issuer", IPAddress.Parse("192.0.2.110"));
        await vault.SignInNewAccountAsync("it-shares-race-recipient", IPAddress.Parse("192.0.2.111"));
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-shares-race-secret");
        await LegacySharing.ShareAsync(issuer.Client, details, "it-shares-race-recipient");
        var share = await LegacySharing.ShareOfAsync(vault, details);
        var before = await LegacySharing.AuditHeadAsync(vault);
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await new SqlServerAuditChainLocker().AcquireAsync(db, "EclipsVault:AuditChain", TimeSpan.FromSeconds(5), default);

        var first = SessionApi.WriteAsync(issuer, HttpMethod.Delete, $"/api/ui/shared-by-me/{share.Id}");
        var second = SessionApi.WriteAsync(issuer, HttpMethod.Delete, $"/api/ui/shared-by-me/{share.Id}");
        try
        {
            await WaitForBothRevocationsToReadTheShareAsync(db);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
        var responses = await Task.WhenAll(first, second);

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NotFound], responses.Select(response => response.StatusCode).Order());
        await UiApiFailureContractTests.AssertProblemAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NotFound),
            404, "share_not_found");
        Assert.False(await db.SecretGrants.AnyAsync(stored => stored.Id == share.Id));
        var row = Assert.Single(await LegacySharing.AuditRowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.SecretShareRevoked, row.Action);
        Assert.Equal(share.SecretId, row.ResourceId);
    }

    private static async Task WaitForBothRevocationsToReadTheShareAsync(EclipsVaultDbContext db)
    {
        // Each request reads the share before it asks for this lock. Waiting for both writers
        // makes the overlap deterministic without replacing services or altering the host.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value] FROM sys.dm_tran_locks
            WHERE resource_type = 'APPLICATION' AND resource_database_id = DB_ID()
                AND request_status = 'WAIT' AND resource_description LIKE '%EclipsVault:AuditChain%'
            """).SingleAsync(deadline.Token) < 2)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
        }
    }
}
