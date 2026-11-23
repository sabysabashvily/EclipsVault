using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EclipsVault.Web.Authorization;

/// <summary>
/// An anonymous sign-in call treats an unusable pending sign-in as none: before the action runs, the
/// cookie of one that expired, or no longer matches its account's credentials, is dropped (ADR-0032).
/// A usable one is left alone and is not the caller's identity here.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ForgetUnusablePendingSignInAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if ((await context.HttpContext.AuthenticateAsync(AuthSchemes.MfaPending)).Failure is not null)
        {
            await context.HttpContext.SignOutAsync(AuthSchemes.MfaPending);
        }

        await next();
    }
}
