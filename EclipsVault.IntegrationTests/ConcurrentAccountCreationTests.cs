using System.Net;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class ConcurrentAccountCreationTests(VaultFixture vault)
{
    [Fact]
    public async Task A_username_created_twice_at_once_makes_one_account_and_shows_the_other_form_the_conflict()
    {
        const string username = "it-race-http-username";
        var admin = await vault.AdminClientAsync();
        var firstForm = await LegacyUsers.CreateFormAsync(admin, username, "Race", "Person");
        var secondForm = await LegacyUsers.CreateFormAsync(admin, username, "Race", "Person");
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // The vault reads committed rows only, so both creates pass the username check and then wait to
        // save behind this uncommitted account with the same username: both have checked before either saves.
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = "Race Placeholder",
            Email = "race.placeholder@eclipsvault.local",
            PasswordHash = new byte[32],
            PasswordSalt = new byte[16],
            ProjectKey = "GLOBAL",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var first = admin.PostAsync(LegacyUsers.CreateUserPage, firstForm);
        var second = admin.PostAsync(LegacyUsers.CreateUserPage, secondForm);
        try
        {
            await WaitingRequests.AtLeastAsync(db, 2);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
        var responses = await Task.WhenAll(first, second);

        Assert.Equal(LegacyUsers.UsersPage, Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Redirect).Headers.Location?.OriginalString);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains($"The username '{username}' is already taken.", WebUtility.HtmlDecode(await conflict.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Users.CountAsync(user => user.Username == username));
    }

    [Fact]
    public async Task One_name_created_twice_at_once_makes_both_accounts_with_the_next_free_addresses()
    {
        var admin = await vault.AdminClientAsync();
        var firstForm = await LegacyUsers.CreateFormAsync(admin, "it-race-http-address-one", "Http", "Twin");
        var secondForm = await LegacyUsers.CreateFormAsync(admin, "it-race-http-address-two", "Http", "Twin");
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // An uncommitted account holds the first address both creates generate, so both have generated
        // it before either saves; rolling it back lets them race for that address.
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "it-race-http-address-holder",
            DisplayName = "Race Placeholder",
            Email = "http.twin.1@eclipsvault.local",
            PasswordHash = new byte[32],
            PasswordSalt = new byte[16],
            ProjectKey = "GLOBAL",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var first = admin.PostAsync(LegacyUsers.CreateUserPage, firstForm);
        var second = admin.PostAsync(LegacyUsers.CreateUserPage, secondForm);
        try
        {
            await WaitingRequests.AtLeastAsync(db, 2);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
        var responses = await Task.WhenAll(first, second);

        Assert.All(responses, response => Assert.Equal(LegacyUsers.UsersPage, response.Headers.Location?.OriginalString));
        db.ChangeTracker.Clear();
        var addresses = await db.Users
            .Where(user => user.Username == "it-race-http-address-one" || user.Username == "it-race-http-address-two")
            .Select(user => user.Email)
            .OrderBy(email => email)
            .ToListAsync();
        Assert.Equal(["http.twin.1@eclipsvault.local", "http.twin.2@eclipsvault.local"], addresses);
    }
}
