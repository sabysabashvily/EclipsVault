using EclipsVault.Web.Authorization;
using EclipsVault.Web.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace EclipsVault.Web.Hosting;

/// <summary>
/// The paths the Angular UI owns (ADR-0014). Each one serves the Angular UI's <c>index.html</c>
/// behind a session; every path not listed here still reaches the Legacy UI. An Area migrates by
/// adding its existing URLs here, so its bookmarks keep working.
///
/// <para>
/// The document lives outside <c>wwwroot</c>, in <c>AngularUi/index.html</c> under the content root,
/// so the static-file middleware cannot serve it anonymously at some other URL. The bundles it loads
/// are ordinary static files under <c>wwwroot/ui/</c>.
/// </para>
/// </summary>
public static class AngularUiRoutes
{
    /// <summary>Where the Angular build writes the document, relative to the content root.</summary>
    public const string IndexPath = "AngularUi/index.html";

    private static readonly string[] Paths =
    [
        // My access
        "/Access",
        "/Access/Index",

        // My activity
        "/Activity",
        "/Activity/Index",

        // Sign-in history
        "/SignIns",
        "/SignIns/Index",

        // Security checkup
        "/Security",
        "/Security/Index",

        // Signed-in devices
        "/Sessions",
        "/Sessions/Index",

        // Your data
        "/Data",
        "/Data/Index",
    ];

    /// <summary>
    /// Maps every owned path. These endpoints outrank the conventional MVC route, because a literal
    /// path takes precedence over <c>{controller}/{action}</c>.
    /// </summary>
    public static void MapAngularUi(this IEndpointRouteBuilder endpoints)
    {
        // A signed-out visitor gets the session cookie's usual redirect to sign-in, with a return URL.
        var session = new AuthorizeAttribute
        {
            AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
            Policy = VaultPolicies.Session,
        };

        foreach (var path in Paths)
        {
            endpoints.MapMethods(path, [HttpMethods.Get, HttpMethods.Head], ServeIndex).RequireAuthorization(session);
        }
    }

    private static IResult ServeIndex(HttpContext context, IWebHostEnvironment environment)
    {
        var index = environment.ContentRootFileProvider.GetFileInfo(IndexPath);
        if (!index.Exists)
        {
            throw new InvalidOperationException(
                $"The Angular UI is not built: {IndexPath} is missing. Run `npm run build` in EclipsVault.AngularUi.");
        }

        // Set this only when serving the Angular document, after authorization and the file check.
        // Redirects, legacy pages, API responses and static bundles retain the existing policy.
        context.Response.Headers["Content-Security-Policy"] = SecurityHeadersMiddleware.AngularContentSecurityPolicy;
        return Results.File(index.PhysicalPath!, "text/html; charset=utf-8");
    }
}
