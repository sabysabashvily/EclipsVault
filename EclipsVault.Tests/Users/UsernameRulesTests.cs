using EclipsVault.Core.Application.Users;
using Xunit;

namespace EclipsVault.Tests.Users;

public class UsernameRulesTests
{
    [Theory]
    [InlineData("vault-admin")]
    [InlineData("first.last_2")]
    [InlineData("abc")]
    [InlineData("a234567890123456789012345678901234567890123456789012345678901234")]
    public void A_username_of_ascii_letters_digits_and_punctuation_is_allowed(string username)
        => Assert.True(UsernameRules.Allows(username));

    [Theory]
    [InlineData("owner@example.test")]
    [InlineData("Øyvind")]
    [InlineData("two words")]
    [InlineData("vault-admin\n")]
    public void A_username_with_an_at_sign_whitespace_or_non_ascii_letters_is_refused(string username)
        => Assert.False(UsernameRules.Allows(username));

    [Theory]
    [InlineData("ab")]
    [InlineData("a2345678901234567890123456789012345678901234567890123456789012345")]
    public void A_username_shorter_than_three_or_longer_than_sixty_four_characters_is_refused(string username)
        => Assert.False(UsernameRules.Allows(username));
}
