using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EclipsVault.Web.Authentication;

public static class SessionClaims
{
    /// <summary>
    /// Re-issues the session cookie with the given claims replaced, keeping every claim not named in
    /// <paramref name="updates"/>. It renews the same session, so the session keeps its id and stamp.
    /// </summary>
    public static async Task RenewReplacingAsync(HttpContext context, params (string Type, string Value)[] updates)
    {
        var replaced = updates.Select(update => update.Type).ToHashSet();
        var claims = context.User.Claims.Where(claim => !replaced.Contains(claim.Type)).ToList();
        claims.AddRange(updates.Select(update => new Claim(update.Type, update.Value)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false });
        context.User = principal;
    }
}
