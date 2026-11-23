using System.Globalization;
using EclipsVault.Core.Application.DynamicSecrets;
using Xunit;

namespace EclipsVault.Tests.DynamicSecrets;

/// <summary>
/// A dynamic credential is rendered into backend DDL as text, because CREATE LOGIN cannot be
/// parameterised. That makes this the injection boundary of the whole feature: if a name or password
/// could carry a quote or a bracket, minting a credential would be arbitrary SQL execution. These
/// pin the refusal, and pin that the generator can only ever produce values that pass it.
/// </summary>
public class CredentialStatementTemplateTests
{
    private static readonly DateTimeOffset Expiry = new(2026, 7, 16, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Renders_the_credential_into_the_statements()
    {
        var sql = CredentialStatementTemplate.Render(
            "CREATE LOGIN [{{name}}] WITH PASSWORD = '{{password}}';", "ev_reader_ab12", "Pw0rd", Expiry);

        Assert.Equal("CREATE LOGIN [ev_reader_ab12] WITH PASSWORD = 'Pw0rd';", sql);
    }

    [Fact]
    public void Renders_every_occurrence_and_the_expiration()
    {
        var sql = CredentialStatementTemplate.Render(
            "CREATE USER [{{name}}] FOR LOGIN [{{name}}]; -- until {{expiration}}", "ev_x_1", "Ab1", Expiry);

        Assert.Equal("CREATE USER [ev_x_1] FOR LOGIN [ev_x_1]; -- until 2026-07-16 12:30:00", sql);
    }

    [Fact]
    public void Renders_a_postgres_role_with_its_quoted_name_and_its_expiry_in_utc()
    {
        var sql = CredentialStatementTemplate.Render(
            """CREATE ROLE "{{name}}" LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}+00';""", "ev_reader_Ab12", "Pw0rd", Expiry);

        Assert.Equal("""CREATE ROLE "ev_reader_Ab12" LOGIN PASSWORD 'Pw0rd' VALID UNTIL '2026-07-16 12:30:00+00';""", sql);
    }

    [Fact]
    public void Renders_the_expiration_with_colons_whatever_the_current_culture()
    {
        var current = CultureInfo.CurrentCulture;
        // Finnish separates hours, minutes and seconds with '.', which a backend would not read as a time.
        CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
        try
        {
            Assert.Equal("until 2026-07-16 12:30:00", CredentialStatementTemplate.Render("until {{expiration}}", "ev_x_1", "Ab1", Expiry));
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
        }
    }

    [Theory]
    [InlineData("ev'; DROP DATABASE Umbra; --")]
    [InlineData("ev\"; DROP ROLE postgres; --")]
    [InlineData("ev]--")]
    [InlineData("ev name")]
    [InlineData("ev-name")]
    [InlineData("")]
    public void Refuses_a_name_that_could_break_out_of_the_ddl(string name)
        => Assert.Throws<ArgumentException>(
            () => CredentialStatementTemplate.Render("CREATE LOGIN [{{name}}];", name, "Ab1", Expiry));

    [Theory]
    [InlineData("pw'; DROP DATABASE Umbra; --")]
    [InlineData("pw'")]
    [InlineData("pw\\")]
    [InlineData("pw_1")]
    [InlineData("")]
    public void Refuses_a_password_that_could_break_out_of_the_quoted_literal(string password)
        => Assert.Throws<ArgumentException>(
            () => CredentialStatementTemplate.Render("... PASSWORD = '{{password}}';", "ev_x_1", password, Expiry));

    [Fact]
    public void The_refusal_never_echoes_the_password()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => CredentialStatementTemplate.Render("'{{password}}'", "ev_x_1", "hunter2';--", Expiry));

        // The value is a live credential even when it is rejected — it must not reach a log or a page.
        Assert.DoesNotContain("hunter2", ex.Message);
    }

    [Fact]
    public void Every_generated_identity_is_renderable()
    {
        foreach (var roleName in new[] { "phoenix_db_reader", "global_db_writer", "weird name!!", "ünïcodé", "" })
        {
            var identity = CredentialMint.NewIdentity(roleName);
            Assert.True(CredentialStatementTemplate.IsRenderableIdentity(identity), $"'{identity}' from '{roleName}'");
        }
    }

    [Fact]
    public void Generated_identities_are_unique_per_lease()
    {
        var identities = Enumerable.Range(0, 200).Select(_ => CredentialMint.NewIdentity("reader")).ToList();
        Assert.Equal(identities.Count, identities.Distinct().Count());
    }

    [Fact]
    public void Every_generated_password_is_renderable_and_meets_sql_server_complexity()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = CredentialMint.NewPassword();

            Assert.True(CredentialStatementTemplate.IsRenderablePassword(password));
            Assert.True(password.Length >= 20);

            // SQL Server wants three of four categories; alphanumeric-only means we must hit all three
            // available ones, or CREATE LOGIN is rejected by the server's password policy.
            Assert.Contains(password, char.IsAsciiLetterLower);
            Assert.Contains(password, char.IsAsciiLetterUpper);
            Assert.Contains(password, char.IsAsciiDigit);
        }
    }
}
