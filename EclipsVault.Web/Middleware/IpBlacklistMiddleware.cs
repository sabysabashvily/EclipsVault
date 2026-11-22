
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

    // Deliberately vague: a honey-token trip must not be distinguishable from any
    // other network block, or the message itself tells the intruder what happened.
    private const string BlockedPageContent = """
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

    // The UI API's version of the page above, in the same words (ADR-0006).
    private const string BlockedProblemDetail =
        "Requests from your network location have been blocked by the vault's intrusion defence. " +
        "The event has been reported to the vault administrators.";

    private const string DevelopmentHint =
        """<p class="footnote">Development note: the block list is held in process memory — restarting the application also clears it.</p>""";

    private readonly RequestDelegate _next;
    private readonly IIpBlacklist _blacklist;
    private readonly ILogger<IpBlacklistMiddleware> _logger;
    private readonly string _blockedContent;

    public IpBlacklistMiddleware(RequestDelegate next, IIpBlacklist blacklist, ILogger<IpBlacklistMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _blacklist = blacklist;
        _logger = logger;
        _blockedContent = BlockedPageContent.Replace("{0}", environment.IsDevelopment() ? DevelopmentHint : string.Empty);
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

            await ServerPage.WriteAsync(context, StatusCodes.Status403Forbidden, "Access blocked", _blockedContent);
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
