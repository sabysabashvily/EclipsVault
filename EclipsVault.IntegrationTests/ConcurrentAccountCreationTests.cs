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
            await WaitForBothCreatesToWaitAsync(db);
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

    /// <summary>
    /// One create waits on the placeholder's username. The other waits there too, or behind the first
    /// for the audit chain the first one holds.
    /// </summary>
    private static async Task WaitForBothCreatesToWaitAsync(EclipsVaultDbContext db)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waitingRequests = db.Database.IsNpgsql() ? PostgresWaitingRequests : SqlServerWaitingRequests;
        while (await db.Database.SqlQueryRaw<int>(waitingRequests).SingleAsync(deadline.Token) < 2)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
        }
    }

    private const string SqlServerWaitingRequests = """
        SELECT COUNT(*) AS [Value] FROM sys.dm_exec_requests
        WHERE database_id = DB_ID() AND blocking_session_id <> 0
        """;

    // Count every waiting lock request, not only those this connection blocks: the second create can
    // wait behind the first for the audit chain. The vault's PostgreSQL server holds only its database.
    // pg_locks is read live, where pg_stat_activity would be read once per transaction.
    private const string PostgresWaitingRequests = """
        SELECT COUNT(DISTINCT pid)::int AS "Value" FROM pg_locks WHERE NOT granted
        """;
}
