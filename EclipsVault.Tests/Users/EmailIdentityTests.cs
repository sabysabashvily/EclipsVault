using EclipsVault.Core.Domain;
using EclipsVault.Core.Domain.Entities;
using Xunit;

namespace EclipsVault.Tests.Users;

public class EmailIdentityTests
{
    [Fact]
    public void An_address_loses_surrounding_white_space_and_uses_invariant_letter_case()
        => Assert.Equal("ALICE.SMITH@EXAMPLE.TEST", EmailIdentity.Normalize("\u00A0\tAlice.Smith@Example.TEST\u3000 "));

    [Fact]
    public void Dots_and_plus_tags_stay_part_of_the_address()
        => Assert.Equal("A.LICE+VAULT@EXAMPLE.TEST", EmailIdentity.Normalize("A.lice+Vault@example.test"));

    [Theory]
    [InlineData("øyvind@example.test", "ØYVIND@EXAMPLE.TEST")]
    [InlineData("σς@example.test", "ΣΣ@EXAMPLE.TEST")]
    [InlineData("i@example.test", "I@EXAMPLE.TEST")]
    public void Unicode_case_variants_use_one_invariant_identity(string email, string normalized)
        => Assert.Equal(normalized, EmailIdentity.Normalize(email));

    [Fact]
    public void Letters_outside_the_basic_multilingual_plane_keep_their_case()
        => Assert.Equal("\U00010428@EXAMPLE.TEST", EmailIdentity.Normalize("\U00010428@example.test"));

    [Fact]
    public void An_account_keeps_its_email_as_entered_and_compares_it_normalized()
    {
        var account = new User { Email = " Alice@Example.Test " };

        Assert.Equal(" Alice@Example.Test ", account.Email);
        Assert.Equal("ALICE@EXAMPLE.TEST", account.NormalizedEmail);
    }
}
