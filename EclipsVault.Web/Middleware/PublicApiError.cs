namespace EclipsVault.Web.Middleware;

/// <summary>
/// The Public API's failure shape, <c>{ "error": "&lt;code&gt;" }</c> with the real status (ADR-0034).
/// Callers pass a fixed code, never an exception's message.
/// </summary>
public static class PublicApiError
{
    private static readonly PathString PublicApiPath = new("/api/v1");

    public static bool IsPublicApi(HttpRequest request) => request.Path.StartsWithSegments(PublicApiPath);

    public static Task WriteAsync(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = code });
    }
}
