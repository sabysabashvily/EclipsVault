using Xunit;
using static EclipsVault.IntegrationTests.ShareIssuerBackfill;

namespace EclipsVault.IntegrationTests;

public sealed class SqlServerShareIssuerBackfillTests(SqlServerShareIssuerBackfill database)
    : IClassFixture<SqlServerShareIssuerBackfill>
{
    [Fact]
    public async Task A_share_gets_the_account_with_its_issuers_username_created_before_it()
        => Assert.Equal(AccountStillHere, await database.IssuerOfAsync(ShareByAnAccountStillHere));

    [Fact]
    public async Task A_share_gets_the_account_with_its_issuers_username_created_at_the_same_instant()
        => Assert.Equal(AccountCreatedAsItShared, await database.IssuerOfAsync(ShareByAnAccountCreatedAtTheSameInstant));

    [Fact]
    public async Task A_share_by_a_deleted_account_gets_no_issuer_when_a_later_account_has_its_username()
        => Assert.Null(await database.IssuerOfAsync(ShareByADeletedAccountWhoseUsernameWasReused));

    [Fact]
    public async Task A_share_whose_issuers_username_matches_no_account_gets_no_issuer()
        => Assert.Null(await database.IssuerOfAsync(ShareByAnUnknownUsername));

    /// <summary>SQL Server's default collation compares usernames case-insensitively, as sign-in does there.</summary>
    [Fact]
    public async Task A_differently_cased_username_names_the_issuer()
        => Assert.Equal(AccountWithDifferentlyCasedUsername, await database.IssuerOfAsync(ShareByADifferentlyCasedUsername));
}

public sealed class PostgresShareIssuerBackfillTests(PostgresShareIssuerBackfill database)
    : IClassFixture<PostgresShareIssuerBackfill>
{
    [Fact]
    public async Task A_share_gets_the_account_with_its_issuers_username_created_before_it()
        => Assert.Equal(AccountStillHere, await database.IssuerOfAsync(ShareByAnAccountStillHere));

    [Fact]
    public async Task A_share_gets_the_account_with_its_issuers_username_created_at_the_same_instant()
        => Assert.Equal(AccountCreatedAsItShared, await database.IssuerOfAsync(ShareByAnAccountCreatedAtTheSameInstant));

    [Fact]
    public async Task A_share_by_a_deleted_account_gets_no_issuer_when_a_later_account_has_its_username()
        => Assert.Null(await database.IssuerOfAsync(ShareByADeletedAccountWhoseUsernameWasReused));

    [Fact]
    public async Task A_share_whose_issuers_username_matches_no_account_gets_no_issuer()
        => Assert.Null(await database.IssuerOfAsync(ShareByAnUnknownUsername));

    /// <summary>
    /// PostgreSQL compares usernames case-sensitively, as sign-in does there, so "Issuer-Cased" and
    /// "issuer-cased" can be two different accounts.
    /// </summary>
    [Fact]
    public async Task A_differently_cased_username_names_no_issuer()
        => Assert.Null(await database.IssuerOfAsync(ShareByADifferentlyCasedUsername));
}
