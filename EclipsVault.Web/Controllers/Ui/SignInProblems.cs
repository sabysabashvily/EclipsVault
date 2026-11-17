using System.Security.Claims;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Models.Ui;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// The sign-in calls' refusals as Problem Details with stable codes (ADR-0006), and the request token
/// each change of identity reissues (ADR-0032). An audit failure is left to the global 503
/// <c>audit_unavailable</c>.
/// </summary>
public static class SignInProblems
{
    public const string Restarted = "Your account changed while you were signing in. Sign in again.";

    public const string PasskeyTimedOut = "Sign-in timed out. Try again.";

    public static JsonResult Problem(int status, string code, string title)
        => new(new SignInProblemResponse { Status = status, Code = code, Title = title })
        {
            StatusCode = status,
            ContentType = "application/problem+json"
        };

    /// <summary>
    /// The sign-in cannot finish under the proof it began with: the pending sign-in is ended, and the
    /// browser gets an anonymous token so Login can start again without a reload.
    /// </summary>
    public static async Task<JsonResult> RestartAsync(HttpContext context, IAntiforgery antiforgery, string title = Restarted)
    {
        await context.SignOutAsync(AuthSchemes.MfaPending);
        IssueTokenFor(context, antiforgery, new ClaimsPrincipal(new ClaimsIdentity()));
        return Problem(StatusCodes.Status409Conflict, "sign_in_restarted", title);
    }

    /// <summary>
    /// Issues the request token for <paramref name="next"/>, the identity the browser's next request
    /// will have. The token binds to <c>HttpContext.User</c>, which signing a cookie in or out leaves as it was.
    /// </summary>
    public static void IssueTokenFor(HttpContext context, IAntiforgery antiforgery, ClaimsPrincipal next)
    {
        context.User = next;
        UiAntiforgery.IssueToken(context, antiforgery);
    }
}
