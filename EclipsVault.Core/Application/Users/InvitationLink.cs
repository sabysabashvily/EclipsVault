using System.Diagnostics.CodeAnalysis;

namespace EclipsVault.Core.Application.Users;

/// <summary>
/// Builds account-activation links solely from the configured <c>Identity:PublicUrl</c>, carrying
/// the token in the fragment so it stays out of request logs, <c>Referer</c> headers, and link
/// scanners (ADR-0033). Never accepts a request host or scheme.
/// </summary>
public static class InvitationLink
{
    private const string ActivatePath = "/Account/Activate";

    public static bool TryParsePublicUrl(string? raw, [NotNullWhen(true)] out Uri? publicUrl)
    {
        publicUrl = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (trimmed.Any(char.IsControl) || trimmed.Contains('?') || trimmed.Contains('#'))
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var allowedScheme = uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
        if (!allowedScheme)
        {
            return false;
        }

        publicUrl = uri;
        return true;
    }

    public static string Build(Uri publicUrl, string token)
    {
        ArgumentNullException.ThrowIfNull(publicUrl);
        if (!TryParsePublicUrl(publicUrl.AbsoluteUri, out var validated))
        {
            throw new ArgumentException("The public URL is not valid for invitation links.", nameof(publicUrl));
        }

        if (string.IsNullOrWhiteSpace(token) || !token.All(IsBase64UrlChar))
        {
            throw new ArgumentException("The invitation token is not valid.", nameof(token));
        }

        var originAndPath = validated.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return $"{originAndPath}{ActivatePath}#{token}";
    }

    public static bool TryBuild(string? publicUrl, string token, [NotNullWhen(true)] out string? link)
    {
        link = null;
        if (!TryParsePublicUrl(publicUrl, out var parsed)
            || string.IsNullOrWhiteSpace(token)
            || !token.All(IsBase64UrlChar))
        {
            return false;
        }

        link = Build(parsed, token);
        return true;
    }

    private static bool IsBase64UrlChar(char c)
        => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
}
