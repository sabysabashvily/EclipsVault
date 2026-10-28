using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Middleware;

/// <summary>
/// The UI API's failure contract (ADR-0006). Under <c>/api/ui/</c> a failure is an RFC 9457
/// Problem Details response with a stable <c>code</c> the Angular UI acts on, never a redirect to
/// an HTML page. Callers pass fixed text, never an exception's message, so no exception detail can
/// reach the body.
/// </summary>
public static class UiApiProblem
{
    private static readonly PathString UiApiPath = new("/api/ui");

    /// <summary>Whether the request is for the UI API. The Legacy UI and the Public API keep their own answers.</summary>
    public static bool IsUiApi(HttpRequest request) => request.Path.StartsWithSegments(UiApiPath);

    public static Task WriteAsync(HttpContext context, int status, string code, string title, string? detail = null)
    {
        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Extensions = { ["code"] = code } };
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }

    /// <summary>Nothing there, or nothing this session may know is there: the two answer alike.</summary>
    public static Task NotFoundAsync(HttpContext context)
        => WriteAsync(context, StatusCodes.Status404NotFound, NotFoundCode, NotFoundTitle);

    public const string NotFoundCode = "not_found";

    public const string NotFoundTitle = "Not found.";

    /// <summary>No session, or a revoked one: the Angular UI sends the browser to sign-in.</summary>
    public static Task UnauthenticatedAsync(HttpContext context)
        => WriteAsync(context, StatusCodes.Status401Unauthorized, "unauthenticated", "Sign in to continue.");

    /// <summary><see cref="UnauthenticatedAsync"/> as an action result, for a UI API action that finds the session unusable.</summary>
    public static ActionResult Unauthenticated() => new Result(UnauthenticatedAsync);

    /// <summary>An unusable session must be ended through the existing protected sign-out flow.</summary>
    public static ActionResult StaleSession() => new Result(context =>
        WriteAsync(context, StatusCodes.Status401Unauthorized, "stale_session", "Sign out and sign in again."));

    /// <summary>A session that may not make this request.</summary>
    public static Task ForbiddenAsync(HttpContext context)
        => WriteAsync(context, StatusCodes.Status403Forbidden, "forbidden", "You are not allowed to do this.");

    /// <summary>An unsafe request without a valid antiforgery token (ADR-0004). Reloading fetches a fresh one.</summary>
    public static Task AntiforgeryRejectedAsync(HttpContext context)
        => WriteAsync(context, StatusCodes.Status400BadRequest, "antiforgery_rejected", "The request could not be verified. Reload the page and try again.");

    /// <summary><see cref="AntiforgeryRejectedAsync"/> as an action result, for the filter that replaces MVC's bare 400.</summary>
    public static ActionResult AntiforgeryRejected() => new Result(AntiforgeryRejectedAsync);

    /// <summary>Over the shared source-address budget for proving credentials. Retrying later is the only fix.</summary>
    public static ActionResult Throttled() => new Result(context =>
        WriteAsync(context, StatusCodes.Status429TooManyRequests, "throttled", "Too many attempts. Wait a few minutes and try again."));

    /// <summary>
    /// A body MVC could not bind. It names the fields, never what was sent: a password or code could be
    /// part of the value that failed to parse.
    /// </summary>
    public static ActionResult ValidationFailed(IEnumerable<string> fields) => new Result(context =>
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        var errors = fields
            .Select(field => field.StartsWith("$.", StringComparison.Ordinal) ? field[2..] : field)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(field => field, _ => new[] { "This field is missing or invalid." });
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "The request could not be read.",
            Extensions = { ["code"] = "validation_failed", ["errors"] = errors }
        };
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    });

    private sealed class Result(Func<HttpContext, Task> write) : ActionResult
    {
        public override Task ExecuteResultAsync(ActionContext context) => write(context.HttpContext);
    }
}
