namespace EclipsVault.Web.Middleware;

/// <summary>Injects defence-in-depth headers on every response.</summary>
public sealed class SecurityHeadersMiddleware
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +      // no <object>/<embed>/<applet> plugin vectors
        "frame-src 'none'; " +       // the app embeds no iframes
        "frame-ancestors 'none'; " + // and refuses to be embedded (clickjacking)
        "form-action 'self'; " +
        "base-uri 'self'";

    // Only the Angular document handler opts in: legacy site.js still writes innerHTML (ADR-0013).
    internal const string AngularContentSecurityPolicy = ContentSecurityPolicy +
        "; require-trusted-types-for 'script'; trusted-types angular angular#bundler";

    /// <summary>
    /// The Angular policy for a document whose form may redirect to <paramref name="origin"/>:
    /// form-action also governs a submission's redirects, so single sign-on's hop to the identity
    /// provider needs it. The origin comes from configuration, never from the request.
    /// </summary>
    internal static string AngularContentSecurityPolicyWithFormTarget(string origin) =>
        AngularContentSecurityPolicy.Replace("form-action 'self';", $"form-action 'self' {origin};", StringComparison.Ordinal);

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        // Keep authenticated vault pages out of the browser's disk cache — and off the back button
        // after sign-out. HTML and every UI API response, errors included, are marked no-store: a
        // UI API reveal is JSON (ADR-0013). Static assets and images (served with their own caching)
        // are untouched. Content-Type is only known once the response starts, so this is deferred
        // to OnStarting.
        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state;
            var contentType = ctx.Response.ContentType;
            if (UiApiProblem.IsUiApi(ctx.Request) ||
                (contentType is not null && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)))
            {
                ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                ctx.Response.Headers.Pragma = "no-cache";
            }
            return Task.CompletedTask;
        }, context);

        var headers = context.Response.Headers;
        headers["Content-Security-Policy"] = ContentSecurityPolicy;
        headers["X-Frame-Options"] = "DENY";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] =
            "camera=(), geolocation=(), microphone=(), payment=(), " +
            "publickey-credentials-get=(self), publickey-credentials-create=(self)";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        // Our resources (avatars, static assets) are only ever loaded same-origin behind auth,
        // so refuse to be pulled into any other site's document.
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        // No legacy Flash/PDF cross-domain policy files are honoured.
        headers["X-Permitted-Cross-Domain-Policies"] = "none";

        return _next(context);
    }
}
