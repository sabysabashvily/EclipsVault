using System.Globalization;

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
public static class CredentialStatementTemplate
{
    public const string NamePlaceholder = "{{name}}";
    public const string PasswordPlaceholder = "{{password}}";
    public const string ExpirationPlaceholder = "{{expiration}}";

    public static string Render(string template, string name, string password, DateTimeOffset expiresAtUtc)
    {
        GuardIdentity(name);
        GuardPassword(password);

        return template
            .Replace(NamePlaceholder, name, StringComparison.Ordinal)
            .Replace(PasswordPlaceholder, password, StringComparison.Ordinal)
            .Replace(ExpirationPlaceholder, expiresAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>True when the value is safe to interpolate into backend DDL.</summary>
    public static bool IsRenderableIdentity(string value)
        => value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>True when the value is safe to interpolate into a quoted literal.</summary>
    public static bool IsRenderablePassword(string value)
        => value.Length > 0 && value.All(char.IsAsciiLetterOrDigit);

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
}
