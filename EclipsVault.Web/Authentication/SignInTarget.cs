using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Authentication;

/// <summary>
/// Where a finished sign-in lands. Form fields, query strings and passkey ceremony state are all
/// untrusted, so the server alone decides, after authentication, and the defaults are literal paths
/// rather than computed from actions that can be retired (ADR-0032).
/// </summary>
public static class SignInTarget
{
    /// <summary>The landing page after every sign-in method without a usable return URL.</summary>
    public const string Default = "/Secrets";

    /// <summary>The landing page after break-glass recovery.</summary>
    public const string AfterBreakGlass = "/";

    /// <returns>An absolute local path; an app-relative <c>~/</c> target is resolved, as the browser cannot.</returns>
    public static string Choose(IUrlHelper url, string? returnUrl, string defaultTarget = Default)
        => url.IsLocalUrl(returnUrl)
            // Location carries only ASCII: LocalRedirect would throw after the session exists.
            && returnUrl.All(c => c is >= ' ' and <= '~')
            && !MayLeadIntoAccount(returnUrl)
                ? returnUrl.TrimStart('~')
                : defaultTarget;

    // A second-factor page accepts only the MFA-pending cookie, which completing sign-in deletes, so a
    // full session sent there is challenged back to Login with the page as its target, and Login sends
    // it straight back. Rather than predict how the browser and Kestrel decode and route a path, judge
    // it as written: refuse every Account page except Denied, the only one a full session is sent to,
    // and any path that reads differently once decoded or resolved. The cost is the default page.
    private static bool MayLeadIntoAccount(string target)
    {
        // The browser treats a backslash as a slash; that is the one rewrite applied here.
        var path = (target.StartsWith('~') ? target[1..] : target).Split('?', '#')[0].Replace('\\', '/');
        if (path.Contains('%') || path.Split('/').Any(segment => segment is "." or ".."))
        {
            return true;
        }
        return path != "/Account/Denied"
            && (path.Equals("/Account", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Account/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/Account;", StringComparison.OrdinalIgnoreCase));
    }
}
