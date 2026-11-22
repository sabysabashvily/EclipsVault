using System.Diagnostics;
using System.Text.Encodings.Web;

namespace EclipsVault.Web.Middleware;

/// <summary>
/// The pages the vault writes itself, without the Angular UI: access blocked, error and not found
/// (ADR-0034). They share the sign-in card, carry no script, and link one static stylesheet. Static
/// files are served before the network block, so a blocked network still loads it.
/// </summary>
public static class ServerPage
{
    private const string Template = """
        <!DOCTYPE html>
        <html lang="en" data-theme="{theme}">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>{title} — EclipsVault</title>
            <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
            <link rel="stylesheet" href="/css/server-page.css" />
        </head>
        <body class="auth-body">
            <main class="auth-wrap">
                <span class="brand auth-brand"><span class="brand-mark" aria-hidden="true"></span><span class="brand-name">EclipsVault</span></span>
                <section class="auth-card">
        {content}
                </section>
            </main>
        </body>
        </html>
        """;

    private const string BackToSafety = """<a class="button primary wide" href="/">Back to safety</a>""";

    // Deliberately vague: a honey-token trip must not be distinguishable from any
    // other network block, or the message itself tells the intruder what happened.
    private const string BlockedContent = """
                    <h1>Access blocked</h1>
                    <p class="muted">
                        Requests from your network location have been blocked by the vault's
                        intrusion defence. The event has been reported to the vault administrators.
                    </p>
                    {0}
                    <a class="button primary wide" href="/Account/Recover">Vault administrator? Recover access</a>
                    <p class="footnote">
                        Recovery requires full multi-factor administrator credentials and is
                        audited. Otherwise, ask an administrator to lift the block from the
                        Networks console.
                    </p>
        """;

    private const string DevelopmentHint =
        """<p class="footnote">Development note: the block list is held in process memory — restarting the application also clears it.</p>""";

    /// <summary>
    /// Writes <paramref name="content"/>, which must be fixed markup, into the card. The theme is the
    /// one the sign-in pages store in their cookie; anything but "light" is dark. Private, so only the
    /// three pages below decide what goes into the card.
    /// </summary>
    private static Task WriteAsync(HttpContext context, int status, string title, string content)
    {
        var theme = context.Request.Cookies["EclipsVault.Theme"] == "light" ? "light" : "dark";
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync(Template
            .Replace("{theme}", theme, StringComparison.Ordinal)
            .Replace("{title}", title, StringComparison.Ordinal)
            .Replace("{content}", content, StringComparison.Ordinal));
    }

    /// <summary>
    /// A failed request: the status with its heading, then <paramref name="message"/>, the failure's
    /// fixed text, never an exception's.
    /// </summary>
    public static Task ErrorAsync(HttpContext context, int status, string message)
    {
        var heading = status switch
        {
            404 => "Not found",
            403 => "Access denied",
            402 => "Licence required",
            409 => "Secret needs a one-time upgrade",
            503 => "Vault unavailable (fail-closed)",
            _ => "Something went wrong"
        };
        var requestId = Activity.Current?.Id ?? context.TraceIdentifier;
        return WriteAsync(context, status, heading, $"""
                        <h1>{status} — {heading}</h1>
                        <p class="muted">{HtmlEncoder.Default.Encode(message)}</p>
                        <p class="muted">Request id: <code>{HtmlEncoder.Default.Encode(requestId)}</code></p>
                        {BackToSafety}
            """);
    }

    /// <summary>A request from a blocked network, with break-glass recovery still offered.</summary>
    public static Task BlockedAsync(HttpContext context, bool development)
        => WriteAsync(context, StatusCodes.Status403Forbidden, "Access blocked",
            BlockedContent.Replace("{0}", development ? DevelopmentHint : string.Empty, StringComparison.Ordinal));

    /// <summary>A path that is not a page.</summary>
    public static Task NotFoundAsync(HttpContext context)
        => WriteAsync(context, StatusCodes.Status404NotFound, "Page not found", $"""
                        <h1>404 — Page not found</h1>
                        <p class="muted">There is no page at this address.</p>
                        {BackToSafety}
            """);
}
