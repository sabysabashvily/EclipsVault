using System.Globalization;
using System.Security.Claims;
using EclipsVault.Web.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EclipsVault.Web.Authentication;

/// <summary>
/// The single builder for the claim sets a sign-in issues. Both the short-lived pending-MFA
/// principal and the full interactive-session principal are minted here, so the exact claims a
/// session carries — the security-critical part — live in one auditable place instead of being
/// hand-assembled at each call site where they could silently drift apart. The read side lives in
/// <see cref="EclipsVault.Web.Extensions.ClaimsPrincipalExtensions"/>.
/// </summary>
public static class VaultClaimsFactory
{
    /// <summary>
    /// The principal issued after the password factor but before TOTP: it carries only enough
    /// identity to complete the second factor, under the short-lived pending-MFA scheme, and the stamp
    /// the password stage passed under, which the rest of the sign-in must keep.
    /// </summary>
    public static ClaimsPrincipal CreatePendingMfaPrincipal(UserDto user, SessionStamp stamp) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                .. StampClaims(stamp)
            ],
            AuthSchemes.MfaPending));

    /// <summary>
    /// The full interactive-session principal granted once both factors pass. It carries the ABAC
    /// attribute claims (clearance, project), the display/avatar hints the UI reads, the strong-auth
    /// timestamp, a per-session id so a single device can be revoked on its own, and the stamp the
    /// cookie validator compares with the account's current one.
    /// </summary>
    public static ClaimsPrincipal CreateSessionPrincipal(UserDto user, Guid sessionId, DateTimeOffset authTime, SessionStamp stamp) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(VaultClaimTypes.Display, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName),
                new Claim(VaultClaimTypes.AvatarVersion, DateTimeOffset.UtcNow.Ticks.ToString()),
                new Claim(VaultClaimTypes.Clearance, ((int)user.Clearance).ToString()),
                new Claim(VaultClaimTypes.Project, user.ProjectKey),
                new Claim(VaultClaimTypes.AuthTime, authTime.ToUnixTimeSeconds().ToString()),
                new Claim(VaultClaimTypes.SessionId, sessionId.ToString()),
                .. StampClaims(stamp)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));

    /// <summary>
    /// The session that replaces <paramref name="current"/> after a credential change it made: a new
    /// session id under the generation the change committed. Every other claim is kept, including the
    /// revocation version, so signing out everywhere after the current session was issued also ends
    /// its replacement, and the strong-auth time, which a credential change does not refresh.
    /// </summary>
    public static ClaimsPrincipal CreateReplacementPrincipal(ClaimsPrincipal current, Guid sessionId, long committedGeneration)
    {
        string[] replaced = [VaultClaimTypes.SessionId, VaultClaimTypes.CredentialGeneration];
        var claims = current.Claims.Where(claim => !replaced.Contains(claim.Type)).ToList();
        claims.Add(new Claim(VaultClaimTypes.SessionId, sessionId.ToString()));
        claims.Add(new Claim(VaultClaimTypes.CredentialGeneration, committedGeneration.ToString(CultureInfo.InvariantCulture)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private static Claim[] StampClaims(SessionStamp stamp) =>
    [
        new Claim(VaultClaimTypes.CredentialGeneration, stamp.CredentialGeneration.ToString(CultureInfo.InvariantCulture)),
        new Claim(VaultClaimTypes.RevocationVersion, stamp.RevocationVersion)
    ];
}
