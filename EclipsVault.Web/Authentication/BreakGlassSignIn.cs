using System.Net;
using EclipsVault.Core.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EclipsVault.Web.Authentication;

/// <summary>Whether break-glass issued a session, and whether that session lifted a block.</summary>
public readonly record struct BreakGlassFinish(bool SessionIssued, bool BlockLifted);

/// <summary>
/// Break-glass lifts a block only after a session exists. The trail then records the lift that
/// actually happened, and it never records a recovery for a sign-in that got no session.
/// </summary>
public sealed class BreakGlassSignIn(SessionIssuer issuer, IIpBlacklist blacklist, IAuditSink audit, ILogger logger)
{
    public async Task<BreakGlassFinish> FinishAsync(
        HttpContext context, UserDto user, SessionStamp stamp, IPAddress? source, CancellationToken ct)
    {
        if (!await issuer.SignInAsync(context, user, stamp, new SignInAudit(AuditAction.LoginSucceeded, "Password + TOTP"), ct))
        {
            return new BreakGlassFinish(false, false);
        }

        var lifted = false;
        try
        {
            lifted = source is not null && await blacklist.UnblockAddressAsync(source, ct);
            await audit.WriteAsync(Recovery(user, source, lifted), ct);
        }
        catch (Exception)
        {
            // The row is the record of the lift. If it cannot be written, the lift does not stand.
            if (lifted && source is not null)
            {
                try
                {
                    await blacklist.BlockAsync(source.ToString(), "Break-glass audit failed; intrusion block restored", CancellationToken.None);
                }
                catch (Exception restoreFailure) when (restoreFailure is not OperationCanceledException)
                {
                    logger.LogError(restoreFailure,
                        "Could not restore the intrusion block for {SourceIp} after break-glass audit failed", source);
                }
            }

            throw;
        }

        return new BreakGlassFinish(true, lifted);
    }

    private static AuditEntry Recovery(UserDto user, IPAddress? source, bool lifted) => new()
    {
        Action = AuditAction.BreakGlassRecovery,
        ResourceType = "User",
        ResourceId = user.Id,
        ActorUserId = user.Id,
        ActorUsername = user.Username,
        Details = $"Break-glass recovery from {source}; block lifted: {lifted}"
    };
}
