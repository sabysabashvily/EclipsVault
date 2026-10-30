using EclipsVault.Core.Domain.Entities;
using Xunit;

namespace EclipsVault.IntegrationTests;

public abstract class MailboxSendLimitTests(AccountChangeDatabases databases)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Six_sends_in_a_sliding_hour_prevent_a_seventh_even_with_new_source_addresses()
    {
        var account = await databases.CreateEnrolledAccountAsync("it-mailbox-hour-limit");
        Assert.True(await ReserveAsync(account, "198.18.88.1", Start));
        Assert.True(await ReserveAsync(account, "198.18.88.2", Start.AddMinutes(2)));
        Assert.True(await ReserveAsync(account, "198.18.88.3", Start.AddMinutes(4)));
        Assert.True(await ReserveAsync(account, "198.18.88.4", Start.AddMinutes(6)));
        Assert.True(await ReserveAsync(account, "198.18.88.5", Start.AddMinutes(8)));
        Assert.True(await ReserveAsync(account, "198.18.88.6", Start.AddMinutes(10)));

        Assert.False(await ReserveAsync(account, "198.18.88.7", Start.AddMinutes(12)));
        Assert.True(await ReserveAsync(account, "198.18.88.8", Start.AddHours(1)));
    }

    [Fact]
    public async Task Different_accounts_share_the_same_source_minute_budget()
    {
        var first = await databases.CreateEnrolledAccountAsync("it-mailbox-source-first");
        var second = await databases.CreateEnrolledAccountAsync("it-mailbox-source-second");
        Assert.True(await ReserveAsync(first, "198.18.88.20", Start));

        Assert.False(await ReserveAsync(second, "198.18.88.20", Start.AddSeconds(59)));
        Assert.True(await ReserveAsync(second, "198.18.88.20", Start.AddMinutes(1)));
    }

    [Fact]
    public async Task Concurrent_accounts_cannot_both_reserve_the_same_source_budget()
    {
        var first = await databases.CreateEnrolledAccountAsync("it-mailbox-source-race-first");
        var second = await databases.CreateEnrolledAccountAsync("it-mailbox-source-race-second");

        var results = await Task.WhenAll(ReserveAsync(first, "198.18.88.30", Start), ReserveAsync(second, "198.18.88.30", Start));

        Assert.Equal([false, true], results.Order());
    }

    private async Task<bool> ReserveAsync(User account, string source, DateTimeOffset now)
    {
        await using var request = databases.NewRequest();
        var loaded = await request.LoadAsync(account.Id);
        return await request.Attempt.RunAsync(loaded, token => request.EmailLimits.TryReserveAsync(account.Id, source, now, token), default);
    }
}

[Collection(SqlServerAccountChangeCollection.Name)]
public sealed class SqlServerMailboxSendLimitTests(SqlServerAccountChangeDatabases databases) : MailboxSendLimitTests(databases);

[Collection(PostgresAccountChangeCollection.Name)]
public sealed class PostgresMailboxSendLimitTests(PostgresAccountChangeDatabases databases) : MailboxSendLimitTests(databases);
