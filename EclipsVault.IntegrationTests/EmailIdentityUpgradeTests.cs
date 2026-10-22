using System.Data.Common;
using EclipsVault.Core.Domain.Exceptions;
using Xunit;
using static EclipsVault.IntegrationTests.EmailIdentityDatabases;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Upgrading a vault whose accounts were made before emails were normalized, on each database.
/// Each refusal runs on a database of its own, because a refused upgrade leaves the old schema.
/// </summary>
public abstract class EmailIdentityUpgradeTests(EmailIdentityDatabases databases)
{
    [Fact]
    public async Task Upgrading_keeps_every_email_spelled_as_it_was_entered()
    {
        Assert.Equal(MixedCaseEmail, await databases.UpgradedEmailOfAsync(MixedCaseAccount));
        Assert.Equal(PaddedEmail, await databases.UpgradedEmailOfAsync(PaddedAccount));
    }

    [Fact]
    public async Task After_upgrading_an_email_in_another_letter_case_finds_its_account()
        => Assert.Equal(MixedCaseAccount, (await databases.UpgradedUsers().FindByEmailAsync("mixed.case@example.test", default))?.Id);

    [Fact]
    public async Task After_upgrading_an_email_with_a_letter_outside_a_to_z_finds_its_account_when_only_a_to_z_change_case()
        => Assert.Equal(UnicodeAccount, (await databases.UpgradedUsers().FindByEmailAsync("Øyvind@EXAMPLE.test", default))?.Id);

    [Fact]
    public async Task After_upgrading_an_email_with_a_character_outside_the_basic_multilingual_plane_finds_its_account()
        => Assert.Equal(SupplementaryAccount, (await databases.UpgradedUsers().FindByEmailAsync("\U00010C28@example.test", default))?.Id);

    [Fact]
    public async Task After_upgrading_an_email_entered_with_surrounding_white_space_finds_its_account_without_it()
        => Assert.Equal(PaddedAccount, (await databases.UpgradedUsers().FindByEmailAsync("Padded@example.test", default))?.Id);

    [Fact]
    public async Task After_upgrading_a_disabled_accounts_email_cannot_be_given_to_another_account_in_another_letter_case()
        => await Assert.ThrowsAsync<EmailAddressTakenException>(() =>
            databases.UpgradedUsers().AddAsync(NewAccount("disabled-copy", "disabled@example.test"), default));

    [Fact]
    public async Task After_upgrading_addresses_that_differ_by_dots_or_plus_tags_stay_separate_accounts()
    {
        var users = databases.UpgradedUsers();

        Assert.Equal(DottedAccount, (await users.FindByEmailAsync("dots.and+tags@example.test", default))?.Id);
        Assert.Equal(UndottedAccount, (await users.FindByEmailAsync("dotsand@example.test", default))?.Id);
    }

    [Fact]
    public async Task After_upgrading_an_account_whose_username_is_its_own_email_signs_in_by_either()
        => Assert.Equal(AccountNamedByItsOwnEmail, (await databases.UpgradedUsers().FindByUsernameOrEmailAsync("self@example.test", default))?.Id);

    [Fact]
    public async Task Upgrading_refuses_emails_that_differ_only_in_case_and_white_space_naming_the_accounts_by_id()
    {
        var database = await databases.CreateBeforeEmailIdentityAsync("refused-case");
        var active = Guid.NewGuid();
        var disabled = Guid.NewGuid();
        await InsertLegacyAccountAsync(database, active, "shared-active", "Shared@Example.Test");
        await InsertLegacyAccountAsync(database, disabled, "shared-disabled", " shared@example.TEST ", disabled: true);

        var refusal = await Assert.ThrowsAnyAsync<DbException>(() => UpgradeAsync(database));

        Assert.Contains("EclipsVault cannot upgrade", refusal.Message);
        Assert.Contains(active.ToString(), refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(disabled.ToString(), refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shared@", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upgrading_refuses_a_username_that_is_another_accounts_email_naming_both_accounts_by_id()
    {
        var database = await databases.CreateBeforeEmailIdentityAsync("refused-alias");
        var named = Guid.NewGuid();
        var addressed = Guid.NewGuid();
        await InsertLegacyAccountAsync(database, named, "Owner@Example.Test", "named@example.test");
        await InsertLegacyAccountAsync(database, addressed, "addressed", "owner@example.test");

        var refusal = await Assert.ThrowsAnyAsync<DbException>(() => UpgradeAsync(database));

        Assert.Contains($"{named} (username) and {addressed} (email)", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upgrading_keeps_emails_that_differ_in_the_case_of_a_letter_outside_a_to_z_as_separate_accounts()
    {
        var database = await databases.CreateBeforeEmailIdentityAsync("accepted-non-ascii-case");
        await InsertLegacyAccountAsync(database, Guid.NewGuid(), "unicode-active", "Øyvind@Example.Test");
        await InsertLegacyAccountAsync(database, Guid.NewGuid(), "unicode-disabled", " øyvind@example.test", disabled: true);

        await UpgradeAsync(database);

        Assert.EndsWith("_NormalizeEmailIdentity", await LatestMigrationAsync(database));
    }

    [Fact]
    public async Task A_refused_upgrade_preserves_every_account()
    {
        var database = await databases.CreateBeforeEmailIdentityAsync("refused-unchanged");
        await InsertLegacyAccountAsync(database, Guid.NewGuid(), "first", "Twice@Example.Test");
        await InsertLegacyAccountAsync(database, Guid.NewGuid(), "second", " twice@example.test");
        var before = await EmailsAsync(database);

        await Assert.ThrowsAnyAsync<DbException>(() => UpgradeAsync(database));

        Assert.Equal(before, await EmailsAsync(database));
    }

    [Fact]
    public async Task A_refused_upgrade_stays_unapplied()
    {
        var database = await databases.CreateBeforeEmailIdentityAsync("refused-unapplied");
        await InsertLegacyAccountAsync(database, Guid.NewGuid(), "first", "Twice@Example.Test");
        await InsertLegacyAccountAsync(database, Guid.NewGuid(), "second", " twice@example.test");

        await Assert.ThrowsAnyAsync<DbException>(() => UpgradeAsync(database));

        Assert.EndsWith("_AddShareIssuerUserId", await LatestMigrationAsync(database));
    }
}

[Collection(SqlServerEmailIdentityCollection.Name)]
public sealed class SqlServerEmailIdentityUpgradeTests(SqlServerEmailIdentityDatabases databases)
    : EmailIdentityUpgradeTests(databases);

[Collection(PostgresEmailIdentityCollection.Name)]
public sealed class PostgresEmailIdentityUpgradeTests(PostgresEmailIdentityDatabases databases)
    : EmailIdentityUpgradeTests(databases);
