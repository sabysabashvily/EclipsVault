using EclipsVault.Web.Middleware;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace EclipsVault.Web.Authentication;

/// <summary>
/// The way a browser forgets its session cookie when the session store is down (ADR-0026). Signing out
/// normally needs the cookie validated first, which needs the store; this route is mapped before every
/// middleware that reads the store or the account, so it works without them. It can do nothing else:
/// it reads no account, revokes nothing on the server and never continues into the rest of the vault.
/// </summary>
public static class LocalSignOut
{
    public const string Path = "/local-sign-out";

    /// <summary>A GET answers this, carrying the request token the POST must send back.</summary>
    public sealed record TokenResponse(string RequestToken);

    public static IApplicationBuilder UseLocalSignOut(this IApplicationBuilder app)
        => app.MapWhen(
            context => context.Request.Path.Equals(Path, StringComparison.OrdinalIgnoreCase),
            branch => branch.Run(HandleAsync));

    private static async Task HandleAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        // Authentication has not run here, so the token is bound to no account. A token issued to a
        // signed-in page names its account and fails this anonymous check.
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        if (HttpMethods.IsGet(context.Request.Method))
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            await context.Response.WriteAsJsonAsync(new TokenResponse(tokens.RequestToken!));
            return;
        }

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "GET, POST";
            return;
        }

        if (!await antiforgery.IsRequestValidAsync(context))
        {
            await UiApiProblem.AntiforgeryRejectedAsync(context);
            return;
        }

        // Deleted the way the cookie handler itself would sign out: same manager, so every chunk of a
        // large cookie goes, and the same path and domain, so the browser matches the cookie it holds.
        var cookies = context.RequestServices.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        cookies.CookieManager.DeleteCookie(context, cookies.Cookie.Name!, cookies.Cookie.Build(context));
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "sign_out_incomplete",
            message = "This browser's cookie was cleared. Other devices may still be signed in."
        });
    }
}
