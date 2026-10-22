using EclipsVault.Core.Domain;
using EclipsVault.Core.Domain.Entities;
using Xunit;

namespace EclipsVault.Tests.Users;

public class EmailIdentityTests
{
    [Fact]
    public void An_address_loses_surrounding_white_space_and_the_case_of_its_letters_a_to_z()
        => Assert.Equal("ALICE.SMITH@EXAMPLE.TEST", EmailIdentity.Normalize("\u00A0\tAlice.Smith@Example.TEST\u3000 "));

    [Fact]
    public void Dots_and_plus_tags_stay_part_of_the_address()
        => Assert.Equal("A.LICE+VAULT@EXAMPLE.TEST", EmailIdentity.Normalize("A.lice+Vault@example.test"));

    [Theory]
    [InlineData("adm\u0131n@example.test", "ADM\u0131N@EXAMPLE.TEST")]
    [InlineData("\u017Fupport@example.test", "\u017FUPPORT@EXAMPLE.TEST")]
    [InlineData("øyvind@example.test", "øYVIND@EXAMPLE.TEST")]
    [InlineData("\U00010428@example.test", "\U00010428@EXAMPLE.TEST")]
    public void Letters_outside_a_to_z_keep_their_case(string email, string normalized)
        => Assert.Equal(normalized, EmailIdentity.Normalize(email));

    [Fact]
    public void An_account_keeps_its_email_as_entered_and_compares_it_normalized()
    {
        var account = new User { Email = " Alice@Example.Test " };

        Assert.Equal(" Alice@Example.Test ", account.Email);
        Assert.Equal("ALICE@EXAMPLE.TEST", account.NormalizedEmail);
    }
}
