using System.Net;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Authentication;

/// <summary>Whether break-glass issued a session, and whether that session lifted a block.</summary>
public readonly record struct BreakGlassFinish(bool SessionIssued, bool BlockLifted);

/// <summary>
/// Break-glass lifts a block only after a session exists, and only after the recovery row that
/// authorizes the lift has committed. A lift that happens then gets its own row. It never records
/// a recovery for a sign-in that got no session.
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

        // The authorization comes first, so a lift never stands without a row. Not cancellable: a row
        // that commits after a cancelled wait would authorize a lift that never ran.
        await audit.WriteAsync(Recovery(user, source), CancellationToken.None);
        if (source is null || !await blacklist.UnblockAddressAsync(source, CancellationToken.None))
        {
            return new BreakGlassFinish(true, false);
        }

        await audit.WriteAsync(Lift(user, source), CancellationToken.None);
        return new BreakGlassFinish(true, true);
    }

    private static AuditEntry Recovery(UserDto user, IPAddress? source) => new()
    {
        Action = AuditAction.BreakGlassRecovery,
        ResourceType = "User",
        ResourceId = user.Id,
        ActorUserId = user.Id,
        ActorUsername = user.Username,
        Details = $"Break-glass recovery from {source}; lifting any intrusion block on it"
    };

    private static AuditEntry Lift(UserDto user, IPAddress source) => new()
    {
        Action = AuditAction.IpRangeUnblocked,
        ResourceType = "TrustedNetwork",
        ResourceName = source.ToString(),
        ActorUserId = user.Id,
        ActorUsername = user.Username,
        Details = "Intrusion-defence block lifted by break-glass recovery"
    };
}
