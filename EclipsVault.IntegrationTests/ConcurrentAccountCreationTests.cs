using System.Net;
using System.Net.Http.Json;
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
    public async Task A_username_created_twice_at_once_makes_one_account_and_answers_the_other_request_the_conflict()
    {
        const string username = "it-race-http-username";
        var admin = await vault.AdminClientAsync();
        var firstForm = UserAccounts.CreateContent(username, "Race", "Person");
        var secondForm = UserAccounts.CreateContent(username, "Race", "Person");
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

        var first = UserAccounts.CreateAsync(admin, firstForm);
        var second = UserAccounts.CreateAsync(admin, secondForm);
        try
        {
            await WaitingRequests.AtLeastAsync(db, 2);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
        var responses = await Task.WhenAll(first, second);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await UiApiFailureContractTests.AssertProblemAsync(conflict, 409, "username_taken");
        Assert.Contains($"The username '{username}' is already taken.", (await conflict.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())!.Title!, StringComparison.Ordinal);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Users.CountAsync(user => user.Username == username));
    }

    [Fact]
    public async Task One_name_created_twice_at_once_makes_both_accounts_with_the_next_free_addresses()
    {
        var admin = await vault.AdminClientAsync();
        var firstForm = UserAccounts.CreateContent("it-race-http-address-one", "Http", "Twin");
        var secondForm = UserAccounts.CreateContent("it-race-http-address-two", "Http", "Twin");
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

        var first = UserAccounts.CreateAsync(admin, firstForm);
        var second = UserAccounts.CreateAsync(admin, secondForm);
        try
        {
            await WaitingRequests.AtLeastAsync(db, 2);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
        var responses = await Task.WhenAll(first, second);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        db.ChangeTracker.Clear();
        var addresses = await db.Users
            .Where(user => user.Username == "it-race-http-address-one" || user.Username == "it-race-http-address-two")
            .Select(user => user.Email)
            .OrderBy(email => email)
            .ToListAsync();
        Assert.Equal(["http.twin.1@eclipsvault.local", "http.twin.2@eclipsvault.local"], addresses);
    }
}
