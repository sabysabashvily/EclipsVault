using System.Reflection;
using EclipsVault.Core.Application.Networks;
using EclipsVault.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EclipsVault.Web.Authorization;

/// <summary>
/// Applies <see cref="IAuthThrottle"/> to the credential-submitting actions on the controller it
/// decorates. Safe (idempotent) methods pass through unmetered.
///
/// This replaces ASP.NET's built-in rate limiter, whose partitions are per-process and therefore
/// grant each replica its own budget. It is a filter rather than middleware so the throttled
/// surface stays declared on the controller it protects, and async so the budget can live in Redis.
///
/// Only unsafe methods other than the existing logout action spend the budget. Logout must remain
/// available when a shared source exhausts its guessing budget, and does not test a credential.
/// The brute-force surface is the POST that submits a
/// password, TOTP, recovery code, or passkey assertion — a GET renders a form or is the SSO
/// callback, and none of them test a caller-supplied secret. Metering those too would let a shared
/// NAT (one office behind one address) 429 its own users on ordinary page loads and SSO returns,
/// spending the guessing budget on traffic that does no guessing.
/// </summary>
public sealed class AuthThrottleFilter : IAsyncActionFilter
{
    private static readonly MethodInfo LogoutAction = typeof(AccountController)
        .GetMethod(nameof(AccountController.Logout), [typeof(CancellationToken)])!;

    private readonly IAuthThrottle _throttle;
    private readonly ILogger<AuthThrottleFilter> _logger;

    public AuthThrottleFilter(IAuthThrottle throttle, ILogger<AuthThrottleFilter> logger)
    {
        _throttle = throttle;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        // Match the resolved method, not a URL or action-name string: every other unsafe action,
        // including an unknown/new action or another controller's Logout, stays metered by default.
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
            || context.ActionDescriptor is ControllerActionDescriptor action && action.MethodInfo == LogoutAction)
        {
            await next();
            return;
        }

        // Normalised so an IPv4-mapped IPv6 caller shares one budget with its plain IPv4 form
        // rather than getting a second one for free.
        var address = context.HttpContext.Connection.RemoteIpAddress;
        var partition = address is null ? "unknown" : NetworkRules.Normalize(address).ToString();

        if (await _throttle.TryAcquireAsync(partition, context.HttpContext.RequestAborted))
        {
            await next();
            return;
        }

        _logger.LogWarning(
            "Authentication request from {SourceIp} refused: over the rate budget for this window", partition);
        context.Result = new StatusCodeResult(StatusCodes.Status429TooManyRequests);
    }
}
