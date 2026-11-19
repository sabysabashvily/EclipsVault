using EclipsVault.Web.Authentication;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Middleware;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;

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

    /// <summary>The second-factor pages, served only to a pending sign-in.</summary>
    private static readonly string[] SecondFactorPaths = ["/Account/Totp", "/Account/RecoveryCode", "/Account/EnrollTotp"];

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

        // Profile
        "/Profile",
        "/Profile/Index",
        "/Profile/ChangePassword",
        "/Profile/RecoveryCodes",

        // Secrets and New secret
        "/Secrets",
        "/Secrets/Index",
        "/Secrets/Create",

        // Shared by me
        "/Secrets/SharedByMe",

        // Shared with me
        "/Secrets/SharedWithMe",

        // Access requests
        "/AccessRequests",
        "/AccessRequests/Index",
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

        // Sign-in (ADR-0032). Login is anonymous, and a signed-in visit goes on to its target. The
        // second-factor pages are served only to a pending sign-in; anyone else is sent to Login.
        endpoints.MapMethods("/Account/Login", [HttpMethods.Get, HttpMethods.Head], ServeLogin).AllowAnonymous();
        var pending = new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.MfaPending };
        foreach (var path in SecondFactorPaths)
        {
            endpoints.MapMethods(path, [HttpMethods.Get, HttpMethods.Head], ServeSecondFactor).RequireAuthorization(pending);
        }

        string[] administratorPaths = ["/Admin/Users", "/Admin/CreateUser", "/Admin/EditUser/{id:guid}", "/Admin/Networks",
            "/ServiceAccounts", "/ServiceAccounts/Index", "/ServiceAccounts/Create", "/ServiceAccounts/Details/{id:guid}",
            "/Audit", "/Audit/Index", "/Notifications", "/Notifications/Index", "/License", "/License/Index", "/Encryption", "/Encryption/Index"];
        foreach (var path in administratorPaths)
        {
            endpoints.MapMethods(path, [HttpMethods.Get, HttpMethods.Head], ServeIndex)
                .RequireAuthorization(new AuthorizeAttribute
                {
                    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
                    Policy = VaultPolicies.AdminOnly,
                });
        }
    }

    private static IResult ServeIndex(HttpContext context, IWebHostEnvironment environment)
        => Results.File(PrepareIndex(context, environment), "text/html; charset=utf-8");

    private static IResult ServeLogin(HttpContext context, IWebHostEnvironment environment, IAntiforgery antiforgery, string? returnUrl)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var url = new UrlHelper(new ActionContext(context, context.GetRouteData(), new ActionDescriptor()));
            return Results.LocalRedirect(SignInTarget.Choose(url, returnUrl));
        }

        // The page's calls need a request token before anyone has signed in (ADR-0004).
        UiAntiforgery.IssueToken(context, antiforgery);
        context.Response.Headers.CacheControl = "no-store";
        // Only a page load takes the one-time notice; a HEAD request or probe must not spend it.
        var notice = HttpMethods.IsGet(context.Request.Method) ? SignInNotice.Take(context) : null;
        var index = PrepareIndex(context, environment);
        return notice is null
            ? Results.File(index, "text/html; charset=utf-8")
            : Results.Content(WithNotice(File.ReadAllText(index), notice), "text/html; charset=utf-8");
    }

    // The token names the pending sign-in, the identity the page's calls carry.
    private static IResult ServeSecondFactor(HttpContext context, IWebHostEnvironment environment, IAntiforgery antiforgery)
    {
        UiAntiforgery.IssueToken(context, antiforgery);
        context.Response.Headers.CacheControl = "no-store";
        return Results.File(PrepareIndex(context, environment), "text/html; charset=utf-8");
    }

    /// <summary>The document with a one-time notice the Login page shows; the notice is one of SignInNotice's constants.</summary>
    private static string WithNotice(string index, string notice)
        => index.Replace("</head>", $"<meta name=\"eclipsvault-sign-in-notice\" content=\"{notice}\" /></head>", StringComparison.Ordinal);

    /// <summary>
    /// Sets the Angular document's Content Security Policy and returns its path, for a response that
    /// serves it after authorization. The Overview's <c>HomeController.Index</c> serves it this way too.
    /// </summary>
    public static string PrepareIndex(HttpContext context, IWebHostEnvironment environment)
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
        return index.PhysicalPath!;
    }
}
