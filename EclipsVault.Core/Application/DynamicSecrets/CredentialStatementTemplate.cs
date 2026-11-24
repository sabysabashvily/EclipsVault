using System.Globalization;
using System.Text.RegularExpressions;

namespace EclipsVault.Core.Application.DynamicSecrets;

/// <summary>
/// Substitutes a minted credential into a role's backend statements.
///
/// The statements are DDL — <c>CREATE LOGIN</c> takes no parameters — so substitution is textual and
/// therefore the injection boundary. Rather than escape, this refuses: a name or password that is
/// not strictly alphanumeric (plus '_' in the name) cannot be rendered at all. Paired with
/// <see cref="CredentialMint"/>, which only ever produces such values, that makes injection a
/// checked invariant instead of a convention someone can quietly break later.
/// </summary>
public static partial class CredentialStatementTemplate
{
    public const string NamePlaceholder = "{{name}}";
    public const string PasswordPlaceholder = "{{password}}";
    public const string ExpirationPlaceholder = "{{expiration}}";

    public static string Render(string template, string name, string password, DateTimeOffset expiresAtUtc)
    {
        GuardIdentity(name);
        GuardPassword(password);

        return Substitute(template, name, password, expiresAtUtc);
    }

    /// <summary>
    /// Substitutes a pre-hashed PostgreSQL <c>SCRAM-SHA-256</c> verifier into <see cref="PasswordPlaceholder"/>
    /// instead of a plaintext password, enforcing the exact RFC 7677 verifier grammar at the DDL boundary.
    /// </summary>
    public static string RenderWithScramVerifier(string template, string name, string scramVerifier, DateTimeOffset expiresAtUtc)
    {
        GuardIdentity(name);
        GuardScramVerifier(scramVerifier);

        return Substitute(template, name, scramVerifier, expiresAtUtc);
    }

    /// <summary>True when the value is safe to interpolate into backend DDL.</summary>
    public static bool IsRenderableIdentity(string value)
        => value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>True when the value is safe to interpolate into a quoted literal.</summary>
    public static bool IsRenderablePassword(string value)
        => value.Length > 0 && value.All(char.IsAsciiLetterOrDigit);

    /// <summary>True when the value matches PostgreSQL's <c>SCRAM-SHA-256</c> verifier format with no characters that can escape a SQL literal.</summary>
    public static bool IsRenderableScramVerifier(string value)
        => !string.IsNullOrEmpty(value) && ScramVerifierPattern().IsMatch(value);

    private static string Substitute(string template, string name, string secretLiteral, DateTimeOffset expiresAtUtc)
        => template
            .Replace(NamePlaceholder, name, StringComparison.Ordinal)
            .Replace(PasswordPlaceholder, secretLiteral, StringComparison.Ordinal)
            .Replace(ExpirationPlaceholder, expiresAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static void GuardIdentity(string name)
    {
        if (!IsRenderableIdentity(name))
        {
            throw new ArgumentException(
                "A dynamic credential name must be non-empty and contain only ASCII letters, digits, or underscores — " +
                "it is interpolated into backend DDL that cannot be parameterised.", nameof(name));
        }
    }

    private static void GuardPassword(string password)
    {
        if (!IsRenderablePassword(password))
        {
            // Deliberately does not echo the value: it is a live credential.
            throw new ArgumentException(
                "A dynamic credential password must be non-empty and strictly alphanumeric — " +
                "it is interpolated into a quoted SQL literal.", nameof(password));
        }
    }

    private static void GuardScramVerifier(string scramVerifier)
    {
        if (!IsRenderableScramVerifier(scramVerifier))
        {
            // Deliberately does not echo the value: it is authentication material.
            throw new ArgumentException(
                "A SCRAM-SHA-256 verifier must match PostgreSQL's SCRAM-SHA-256$<iterations>:<salt>$<storedKey>:<serverKey> format.",
                nameof(scramVerifier));
        }
    }

    [GeneratedRegex(@"^SCRAM-SHA-256\$[1-9][0-9]*:[A-Za-z0-9+/]+=*\$[A-Za-z0-9+/]{43}=:[A-Za-z0-9+/]{43}=$")]
    private static partial Regex ScramVerifierPattern();
}
