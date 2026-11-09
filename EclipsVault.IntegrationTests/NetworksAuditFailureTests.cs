using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Locking;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A trusted-network change or a lifted block happens only with its audit row. Each test sets up with
/// auditing working, then rejects audit writes, so the change is the step that fails.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class NetworksAuditFailureTests(AuditFailureFixture failure)
{
    [Fact]
    public async Task An_add_that_cannot_be_recorded_trusts_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await NetworksApi.AddAsync(admin, "203.0.113.61", "Unrecorded");

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.Null(await NetworksApi.FindAsync(vault, "203.0.113.61/32"));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        // Nothing the failed add staged is committed by the next save.
        Assert.Null(await NetworksApi.FindAsync(vault, "203.0.113.61/32"));
    }

    [Fact]
    public async Task Trusting_the_current_address_that_cannot_be_recorded_trusts_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.SignInNewAccountAsync("it-net-unrecorded-trust", IPAddress.Parse("198.18.114.61"), ClearanceLevel.TopSecret);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await NetworksApi.TrustCurrentAddressAsync(admin.Client);

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.Null(await NetworksApi.FindAsync(vault, "198.18.114.61/32"));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_removal_that_cannot_be_recorded_keeps_the_range_trusted()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        (await NetworksApi.AddAsync(admin, "203.0.113.62", "Kept")).EnsureSuccessStatusCode();
        var stored = await NetworksApi.FindAsync(vault, "203.0.113.62/32");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await NetworksApi.RemoveAsync(admin, stored!.Id);

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            var kept = await NetworksApi.FindAsync(vault, "203.0.113.62/32");
            Assert.Equal((stored.Id, "Kept"), (kept!.Id, kept.Label));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
            await NetworksApi.ForgetAsync(vault, "203.0.113.62/32");
        }
    }

    [Fact]
    public async Task A_lift_that_cannot_be_recorded_restores_the_original_block()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync("198.51.100.113", "Honey-token 'Unblock regression' tripped");
        var original = Assert.Single(await blacklist.ListAsync());
        Assert.Equal(403, (await vault.SendFromAsync(IPAddress.Parse("198.51.100.113"), "/")).Response.StatusCode);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await NetworksApi.LiftAsync(admin, "198.51.100.113/32");

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.Equal(original, Assert.Single(await blacklist.ListAsync()));
            Assert.Equal(403, (await vault.SendFromAsync(IPAddress.Parse("198.51.100.113"), "/")).Response.StatusCode);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
            await blacklist.UnblockAsync("198.51.100.113/32");
        }
    }

    [Fact]
    public async Task A_lift_that_can_be_neither_recorded_nor_restored_says_so_and_keeps_the_newer_block()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync("198.51.100.117", "Honey-token 'Original' tripped");
        var before = await AuditTrail.HeadAsync(vault);
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();

        await failure.RejectAuditWritesAsync();
        try
        {
            // Holding the audit chain keeps the lift waiting on its audit write after the block is gone,
            // so the range can be blocked again before the failed write tries to restore the original.
            Task<HttpResponseMessage> lift;
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await vault.Services.GetRequiredService<IAuditChainLocker>()
                    .AcquireAsync(db, "EclipsVault:AuditChain", TimeSpan.FromSeconds(10), default);
                lift = NetworksApi.LiftAsync(admin, "198.51.100.117/32");
                await WaitingRequests.AtLeastAsync(db, 1);
                Assert.Empty(await blacklist.ListAsync());
                await blacklist.BlockAsync("198.51.100.117", "Honey-token 'Newer' tripped");
                await transaction.RollbackAsync();
            }

            var problem = await ProfileApi.AssertProblemAsync(await lift, 503, "unblock_unrecorded");

            Assert.Equal("The block was lifted, but recording and restoring it failed. Check the range before retrying.",
                problem.GetProperty("title").GetString());
            Assert.Equal("Honey-token 'Newer' tripped", Assert.Single(await blacklist.ListAsync()).Reason);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
            await blacklist.UnblockAsync("198.51.100.117/32");
        }
    }
}
