using System.Net;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class ConcurrentServiceAccountCreationTests(VaultFixture vault)
{
    [Fact]
    public async Task A_name_created_twice_at_once_makes_one_service_account_and_shows_the_other_form_the_conflict()
    {
        const string name = "it-race-http-service-account";
        var admin = await vault.AdminClientAsync();
        var firstForm = await LegacyServiceAccounts.CreateFormAsync(admin, name);
        var secondForm = await LegacyServiceAccounts.CreateFormAsync(admin, name);
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // The vault reads committed rows only, so both creates pass the name check and then wait to save
        // behind this uncommitted service account with the same name: both have checked before either saves.
        db.ServiceAccounts.Add(new ServiceAccount { Id = Guid.NewGuid(), Name = name, ProjectKey = "GLOBAL", CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var first = admin.PostAsync(LegacyServiceAccounts.CreatePage, firstForm);
        var second = admin.PostAsync(LegacyServiceAccounts.CreatePage, secondForm);
        try
        {
            await WaitingRequests.AtLeastAsync(db, 2);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
        var responses = await Task.WhenAll(first, second);

        var created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Redirect);
        Assert.StartsWith("/ServiceAccounts/Details/", created.Headers.Location?.OriginalString, StringComparison.Ordinal);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains($"A service account named '{name}' already exists.", WebUtility.HtmlDecode(await conflict.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.ServiceAccounts.CountAsync(account => account.Name == name));
    }
}
