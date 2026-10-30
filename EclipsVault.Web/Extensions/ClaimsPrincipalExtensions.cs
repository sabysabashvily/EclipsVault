using System.Globalization;
using System.Security.Claims;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Authorization;

namespace EclipsVault.Web.Extensions;

/// <summary>
/// The single source of truth for reading the claims EclipsVault stamps onto a signed-in
/// principal. Keeping parsing here gives controllers, authorization handlers and the audit context
/// the same claim names and fail-closed defaults. The write side lives in
/// <see cref="EclipsVault.Web.Authentication.VaultClaimsFactory"/>.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static DateTimeOffset GetLastStrongAuthUtc(this ClaimsPrincipal principal)
    {
        long Read(string claimType) => long.TryParse(principal.FindFirstValue(claimType), out var seconds) ? seconds : 0;
        return DateTimeOffset.FromUnixTimeSeconds(Math.Max(Read(VaultClaimTypes.AuthTime), Read(VaultClaimTypes.StepUpTime)));
    }

    /// <summary>
    /// The signed-in user's id, or <see cref="Guid.Empty"/> when absent. Empty is the fail-closed
    /// default: it scopes a self-service read to no rows rather than risking another user's data.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
        => principal.GetUserIdOrNull() ?? Guid.Empty;

    /// <summary>
    /// The signed-in user's id, or null when absent — for flows that must branch on presence, such
    /// as the pending-MFA stage that runs before the full session identity exists.
    /// </summary>
    public static Guid? GetUserIdOrNull(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>The immutable login username (the audit anchor), or empty when unauthenticated.</summary>
    public static string GetUsername(this ClaimsPrincipal principal)
        => principal.Identity?.Name ?? string.Empty;

    /// <summary>The user-editable name to show, falling back to the username when none is set.</summary>
    public static string GetDisplayName(this ClaimsPrincipal principal)
        => principal.FindFirstValue(VaultClaimTypes.Display) ?? principal.GetUsername();

    /// <summary>Changes whenever the avatar does, so an avatar URL carrying it is never served stale; "0" before any change.</summary>
    public static string GetAvatarVersion(this ClaimsPrincipal principal)
        => principal.FindFirstValue(VaultClaimTypes.AvatarVersion) ?? "0";

    /// <summary>This device's session id, or null when the principal carries none (e.g. an API-key caller).</summary>
    public static Guid? GetSessionId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(VaultClaimTypes.SessionId), out var id) ? id : null;

    /// <summary>
    /// The stamp the session or pending sign-in was issued under, or null when it carries none. A
    /// cookie without one cannot be fenced, so it is refused rather than trusted.
    /// </summary>
    public static SessionStamp? GetSessionStampOrNull(this ClaimsPrincipal principal)
        => long.TryParse(principal.FindFirstValue(VaultClaimTypes.CredentialGeneration), NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
           && principal.FindFirstValue(VaultClaimTypes.RevocationVersion) is { Length: > 0 } version
            ? new SessionStamp(generation, version)
            : null;

    /// <summary>The session a Profile operation belongs to, or null when the principal lacks part of it.</summary>
    public static ProfileSession? GetProfileSessionOrNull(this ClaimsPrincipal principal)
        => principal.GetUserIdOrNull() is { } accountId && principal.GetSessionId() is { } sessionId
           && principal.GetSessionStampOrNull() is { } stamp
            ? new ProfileSession(accountId, sessionId, stamp)
            : null;

    /// <summary>The project the principal is scoped to, or empty when none is present.</summary>
    public static string GetProject(this ClaimsPrincipal principal)
        => principal.FindFirstValue(VaultClaimTypes.Project) ?? string.Empty;

    /// <summary>
    /// The clearance the principal carries, or null when the claim is absent or unparseable — for
    /// the two enforcement points (the ABAC handler, the "My access" page) that must tell a stale
    /// session apart from a low one and send it to sign in again rather than assume anything.
    /// </summary>
    public static ClearanceLevel? GetClearanceOrNull(this ClaimsPrincipal principal)
        => int.TryParse(principal.FindFirstValue(VaultClaimTypes.Clearance), out var value) && Enum.IsDefined((ClearanceLevel)value)
            ? (ClearanceLevel)value
            : null;

    /// <summary>
    /// The clearance the principal carries, defaulting to the lowest real clearance
    /// (<see cref="ClearanceLevel.Standard"/>) when absent — the fail-closed floor for a display or
    /// a "may I classify this high?" check, where assuming least privilege is the safe direction.
    /// </summary>
    public static ClearanceLevel GetClearance(this ClaimsPrincipal principal)
        => principal.GetClearanceOrNull() ?? ClearanceLevel.Standard;

    /// <summary>
    /// True only when the principal holds TopSecret clearance — the vault's single administrator
    /// bar. This is the one place that definition lives: an absent or lower clearance is not an
    /// admin, so it fails closed. Mirrors the <c>AdminOnly</c> policy for the many call sites that
    /// need the boolean inline rather than as a route gate.
    /// </summary>
    public static bool IsAdmin(this ClaimsPrincipal principal)
        => principal.GetClearanceOrNull() == ClearanceLevel.TopSecret;
}
