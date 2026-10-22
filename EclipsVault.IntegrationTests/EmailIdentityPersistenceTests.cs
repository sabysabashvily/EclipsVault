using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence.Repositories;
using Xunit;
using static EclipsVault.IntegrationTests.EmailIdentityDatabases;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Email lookups and the database's uniqueness agree on each database: an address one lookup
/// treats as the same identity is one the database refuses to give a second account.
/// Every test makes accounts with names of its own in one shared database.
/// </summary>
public abstract class EmailIdentityPersistenceTests(EmailIdentityDatabases databases)
{
    [Fact]
    public async Task A_second_account_cannot_be_saved_with_an_email_the_lookup_treats_as_the_first_accounts()
    {
        var first = NewAccount("it-email-first", "First.Account@Example.Test");
        await databases.CurrentUsers().AddAsync(first, default);

        await Assert.ThrowsAsync<EmailAddressTakenException>(() =>
            databases.CurrentUsers().AddAsync(NewAccount("it-email-second", " first.account@EXAMPLE.test "), default));
    }

    [Fact]
    public async Task An_email_with_different_case_and_surrounding_whitespace_finds_the_original_account()
    {
        var account = NewAccount("it-lookup-holder", "Lookup.Holder@Example.Test");
        await databases.CurrentUsers().AddAsync(account, default);

        Assert.Equal(account.Id, (await databases.CurrentUsers().FindByEmailAsync(" lookup.holder@EXAMPLE.test ", default))?.Id);
    }

    [Fact]
    public async Task Taking_another_accounts_email_past_every_check_is_refused_by_the_database_as_a_generic_conflict()
    {
        await databases.CurrentUsers().AddAsync(NewAccount("it-email-holder", "Held@Example.Test"), default);
        var taker = NewAccount("it-email-taker", "taker@example.test");
        await databases.CurrentUsers().AddAsync(taker, default);
        var users = databases.CurrentUsers();
        var stored = await users.FindByIdAsync(taker.Id, default);
        stored!.Email = "held@example.test";

        var conflict = await Assert.ThrowsAsync<EmailAddressTakenException>(() => users.UpdateAsync(stored, default));

        Assert.Equal("That email address is already used by another account.", conflict.Message);
    }

    [Fact]
    public async Task An_account_can_change_the_letter_case_of_its_own_email()
    {
        var account = NewAccount("it-email-recased", "recased@example.test");
        await databases.CurrentUsers().AddAsync(account, default);
        var users = databases.CurrentUsers();
        var stored = await users.FindByIdAsync(account.Id, default);
        stored!.Email = "ReCased@Example.Test";

        await users.UpdateAsync(stored, default);

        Assert.Equal("ReCased@Example.Test", (await databases.CurrentUsers().FindByEmailAsync("recased@example.test", default))?.Email);
    }

    [Fact]
    public async Task A_space_inside_a_username_does_not_reserve_an_email_without_that_space()
    {
        await databases.CurrentUsers().AddAsync(NewAccount("it-internal space@example.test", "it-spaced-name@example.test"), default);

        Assert.False(await databases.CurrentUsers().IsEmailTakenAsync("it-internalspace@example.test", Guid.Empty, default));
    }

    [Fact]
    public async Task The_email_lookup_never_finds_an_account_by_its_username()
    {
        await databases.CurrentUsers().AddAsync(NewAccount("it-only-a-name@example.test", "it-named@example.test"), default);

        Assert.Null(await databases.CurrentUsers().FindByEmailAsync("it-only-a-name@example.test", default));
    }

    [Fact]
    public async Task The_combined_lookup_finds_an_account_by_its_username_or_its_email_in_another_letter_case()
    {
        var account = NewAccount("it-combined", "Combined@Example.Test");
        await databases.CurrentUsers().AddAsync(account, default);

        Assert.Equal(account.Id, (await databases.CurrentUsers().FindByUsernameOrEmailAsync("it-combined", default))?.Id);
        Assert.Equal(account.Id, (await databases.CurrentUsers().FindByUsernameOrEmailAsync("combined@example.test", default))?.Id);
    }

    [Fact]
    public async Task The_combined_lookup_finds_no_account_when_one_accounts_username_is_anothers_email()
    {
        await databases.CurrentUsers().AddAsync(NewAccount("it-alias@example.test", "it-alias-named@example.test"), default);
        await databases.CurrentUsers().AddAsync(NewAccount("it-alias-addressed", "IT-Alias@Example.Test"), default);

        Assert.Null(await databases.CurrentUsers().FindByUsernameOrEmailAsync("it-alias@example.test", default));
    }

    [Fact]
    public async Task An_email_is_taken_when_another_account_has_it_as_its_username_in_another_letter_case()
    {
        await databases.CurrentUsers().AddAsync(NewAccount("it-ops@example.test", "it-ops-named@example.test"), default);
        var other = NewAccount("it-ops-other", "it-ops-other@example.test");
        await databases.CurrentUsers().AddAsync(other, default);

        Assert.True(await databases.CurrentUsers().IsEmailTakenAsync("IT-Ops@Example.Test", other.Id, default));
    }

    [Fact]
    public async Task An_accounts_own_email_in_another_letter_case_is_not_taken_from_it()
    {
        var account = NewAccount("it-own-email", "own.email@example.test");
        await databases.CurrentUsers().AddAsync(account, default);

        Assert.False(await databases.CurrentUsers().IsEmailTakenAsync("Own.Email@Example.Test", account.Id, default));
    }

    [Fact]
    public async Task Unicode_case_variants_cannot_be_given_to_a_second_account()
    {
        var account = NewAccount("it-unicode-holder", "σς.Øyvind@Example.Test");
        await databases.CurrentUsers().AddAsync(account, default);

        await Assert.ThrowsAsync<EmailAddressTakenException>(() =>
            databases.CurrentUsers().AddAsync(NewAccount("it-unicode-taker", "ΣΣ.øyvind@example.test"), default));
    }

    [Fact]
    public async Task Unicode_case_variants_find_the_original_account()
    {
        var account = NewAccount("it-unicode-lookup", "σς.Lookup.Øyvind@Example.Test");
        await databases.CurrentUsers().AddAsync(account, default);

        Assert.Equal(account.Id, (await databases.CurrentUsers().FindByEmailAsync("ΣΣ.lookup.øyvind@example.test", default))?.Id);
    }
}

[Collection(SqlServerEmailIdentityCollection.Name)]
public sealed class SqlServerEmailIdentityPersistenceTests(SqlServerEmailIdentityDatabases databases)
    : EmailIdentityPersistenceTests(databases);

[Collection(PostgresEmailIdentityCollection.Name)]
public sealed class PostgresEmailIdentityPersistenceTests(PostgresEmailIdentityDatabases databases)
    : EmailIdentityPersistenceTests(databases);
