using System.Net;
using System.Text.Json;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class ConcurrentNetworkAddTests(VaultFixture vault)
{
    [Fact]
    public async Task A_range_added_twice_at_once_is_trusted_once_and_the_other_add_is_the_conflict()
    {
        const string cidr = "203.0.113.71/32";
        var admin = await vault.AdminClientAsync();
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);
        var before = await AuditTrail.HeadAsync(vault);
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // The vault reads committed rows only, so both adds pass the existing-range check and then wait to save
        // behind this uncommitted row with the same range: both have checked before either saves.
        db.TrustedNetworks.Add(new TrustedNetwork { Id = Guid.NewGuid(), Cidr = cidr, Label = "Uncommitted", AddedBy = "race", CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var first = UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, NetworksApi.Path, NetworksApi.AddContent("203.0.113.71", "First"));
        var second = UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, NetworksApi.Path, NetworksApi.AddContent("203.0.113.71", "Second"));
        try
        {
            await WaitingRequests.AtLeastAsync(db, 2);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
        var responses = await Task.WhenAll(first, second);

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            using var problem = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
            Assert.Equal("network_exists", problem.RootElement.GetProperty("code").GetString());
            Assert.Equal("The range '203.0.113.71/32' is already trusted.", problem.RootElement.GetProperty("title").GetString());
            db.ChangeTracker.Clear();
            var saved = await db.TrustedNetworks.AsNoTracking().SingleAsync(network => network.Cidr == cidr);
            var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before));
            Assert.Equal((AuditAction.TrustedNetworkAdded, saved.Id), (row.Action, row.ResourceId));
        }
        finally
        {
            await NetworksApi.ForgetAsync(vault, cidr);
        }
    }
}
