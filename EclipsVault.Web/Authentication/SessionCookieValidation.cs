using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EclipsVault.Web.Authentication;

/// <summary>
/// Decides on every request whether a session cookie still belongs to a live session, so revocation
/// takes effect on the next request whatever the cookie's lifetime. Renewing a cookie copies its
/// claims, so a renewed cookie keeps the stamp it was issued under and never outlives a later revocation.
/// </summary>
public static class SessionCookieValidation
{
    public static async Task ValidatePendingPrincipalAsync(CookieValidatePrincipalContext context)
    {
        if (!await HasCurrentStampAsync(context))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
        }
    }

    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        if (!await IsLiveAsync(context))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private static async Task<bool> IsLiveAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal?.GetUserIdOrNull() is not { } userId
            || principal.GetSessionId() is not { } sessionId
            || principal.GetSessionStampOrNull() is not { } stamp)
        {
            return false;
        }

        if (!await HasCurrentStampAsync(context))
        {
            return false;
        }

        var services = context.HttpContext.RequestServices;
        var registry = services.GetRequiredService<ISessionRegistry>();
        if (await registry.IsRevokedAsync(userId, sessionId))
        {
            return false;
        }

        // Last-seen is best-effort metadata: never fail a valid session over it.
        try
        {
            await registry.RecordSeenAsync(SessionIssuer.ObservationOf(context.HttpContext, userId, sessionId, stamp));
        }
        catch (Exception ex)
        {
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("SessionRegistry")
                .LogDebug(ex, "Non-fatal: could not record session activity");
        }

        return true;
    }

    private static async Task<bool> HasCurrentStampAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.GetUserIdOrNull() is not { } userId
            || context.Principal.GetSessionStampOrNull() is not { } stamp)
        {
            return false;
        }

        var services = context.HttpContext.RequestServices;
        // Missing store state never matches: it may have lost the sign-out that retired this version.
        if (await services.GetRequiredService<ISessionRevocationService>().FindAsync(userId) != stamp.RevocationVersion)
        {
            return false;
        }

        // The account, and with it the credential generation, is durable where the session store is not.
        var account = await services.GetRequiredService<IUserRepository>().FindByIdAsync(userId, context.HttpContext.RequestAborted);
        if (account is not { IsDisabled: false } || account.CredentialGeneration != stamp.CredentialGeneration)
        {
            return false;
        }

        return true;
    }
}
