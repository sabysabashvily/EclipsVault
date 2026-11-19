namespace EclipsVault.Web.Authentication;

/// <summary>
/// A one-time notice for the next sign-in page, carried in a cookie only the Login document reads
/// and clears. A URL anyone could type cannot claim it, as a query code could.
/// </summary>
public static class SignInNotice
{
    /// <summary>The account's own authenticator reset finished, and its next sign-in enrolls a new one.</summary>
    public const string AuthenticatorReset = "authenticator_reset";

    private const string CookieName = "EclipsVault.SignInNotice";
    private const string LoginPath = "/Account/Login";

    public static void HandOff(HttpResponse response, string notice)
        => response.Cookies.Append(CookieName, notice, Options(TimeSpan.FromMinutes(10)));

    /// <summary>The notice handed to this sign-in, which no later sign-in sees, or null when there is none.</summary>
    public static string? Take(HttpContext context)
    {
        if (context.Request.Cookies[CookieName] is not { } notice)
        {
            return null;
        }

        context.Response.Cookies.Delete(CookieName, Options(maxAge: null));
        return notice == AuthenticatorReset ? notice : null;
    }

    private static CookieOptions Options(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = LoginPath,
        MaxAge = maxAge,
    };
}
