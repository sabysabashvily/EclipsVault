using System.Net;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Authentication;

/// <summary>Whether break-glass issued a session, and whether that session lifted a block.</summary>
public readonly record struct BreakGlassFinish(bool SessionIssued, bool BlockLifted);

/// <summary>
/// Break-glass lifts a block only after a session exists, and only after the recovery row that
/// records the lift has committed. It never records a recovery for a sign-in that got no session.
/// </summary>
public sealed class BreakGlassSignIn(SessionIssuer issuer, IIpBlacklist blacklist, IAuditSink audit)
{
    public async Task<BreakGlassFinish> FinishAsync(
        HttpContext context, UserDto user, SessionStamp stamp, IPAddress? source, CancellationToken ct)
    {
        if (!await issuer.SignInAsync(context, user, stamp, new SignInAudit(AuditAction.LoginSucceeded, "Password + TOTP"), ct))
        {
            return new BreakGlassFinish(false, false);
        }

        var blocked = source is not null && await blacklist.IsBlockedAsync(source, ct);
        // The row comes first, so a lift never stands without it. Not cancellable: a row that commits
        // after a cancelled wait would record a lift that never ran.
        await audit.WriteAsync(Recovery(user, source, blocked), CancellationToken.None);
        var lifted = blocked && await blacklist.UnblockAddressAsync(source!, CancellationToken.None);
        return new BreakGlassFinish(true, lifted);
    }

    private static AuditEntry Recovery(UserDto user, IPAddress? source, bool lifting) => new()
    {
        Action = AuditAction.BreakGlassRecovery,
        ResourceType = "User",
        ResourceId = user.Id,
        ActorUserId = user.Id,
        ActorUsername = user.Username,
        Details = $"Break-glass recovery from {source}; block lifted: {lifting}"
    };
}
