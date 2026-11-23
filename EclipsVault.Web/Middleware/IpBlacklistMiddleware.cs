
namespace EclipsVault.Web.Middleware;

/// <summary>
/// Drops every request originating from a blacklisted range (populated by the
/// honey-token intrusion response) before it reaches routing or authentication.
/// Break-glass recovery is exempt, as the Recover page and its UI API call and nothing else
/// (ADR-0032): it demands full administrator multi-factor credentials before lifting a block,
/// so a locked-out admin always has a way back in while a low-privilege intruder does not.
/// </summary>
public sealed class IpBlacklistMiddleware
{
    private static readonly PathString RecoveryPath = new("/Account/Recover");
    private static readonly PathString RecoveryCall = new("/api/ui/auth/recover");

    // The UI API's version of the blocked page, in the same words (ADR-0006).
    // Deliberately vague, like the page: a honey-token trip must look like any other block.
    private const string BlockedProblemDetail =
        "Requests from your network location have been blocked by the vault's intrusion defence. " +
        "The event has been reported to the vault administrators.";

    private readonly RequestDelegate _next;
    private readonly IIpBlacklist _blacklist;
    private readonly ILogger<IpBlacklistMiddleware> _logger;
    private readonly bool _development;

    public IpBlacklistMiddleware(RequestDelegate next, IIpBlacklist blacklist, ILogger<IpBlacklistMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _blacklist = blacklist;
        _logger = logger;
        _development = environment.IsDevelopment();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sourceIp = context.Connection.RemoteIpAddress;
        if (sourceIp is not null
            && !IsRecovery(context.Request)
            && await _blacklist.IsBlockedAsync(sourceIp, context.RequestAborted))
        {
            _logger.LogWarning("Rejected request to {Path} from blacklisted source {SourceIp}", context.Request.Path, sourceIp);
            if (UiApiProblem.IsUiApi(context.Request))
            {
                await UiApiProblem.WriteAsync(context, StatusCodes.Status403Forbidden, "blocked", "Access blocked", BlockedProblemDetail);
                return;
            }

            if (PublicApiError.IsPublicApi(context.Request))
            {
                await PublicApiError.WriteAsync(context, StatusCodes.Status403Forbidden, "blocked");
                return;
            }

            await ServerPage.BlockedAsync(context, _development);
            return;
        }

        await _next(context);
    }

    // Exact paths and methods, so no other route under the page, the retired form's POST included, escapes the block.
    private static bool IsRecovery(HttpRequest request)
        => (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                && request.Path.Equals(RecoveryPath, StringComparison.OrdinalIgnoreCase)
            || HttpMethods.IsPost(request.Method) && request.Path.Equals(RecoveryCall, StringComparison.OrdinalIgnoreCase);
}
