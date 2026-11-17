using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Web.Authentication;

/// <summary>Whether break-glass issued a session, and whether that session lifted a block.</summary>
public readonly record struct BreakGlassFinish(bool SessionIssued, bool BlockLifted);

/// <summary>
/// Break-glass lifts a block only after a session exists, and only after the recovery row that
/// authorizes the lift has committed. A lift that happens then gets its own row. It never records
/// a recovery for a sign-in that got no session. A lift whose row is refused is undone; the
/// recorded session stays, but no access is granted that the trail does not record (ADR-0032).
/// </summary>
public sealed class BreakGlassSignIn(SessionIssuer issuer, IIpBlacklist blacklist, IAuditSink audit)
{
    /// <exception cref="AuditWriteFailedException">A row was refused; the session, if issued, stays, and every block is as it was.</exception>
    /// <exception cref="RecoveryIncompleteException">The lift could not be settled; the source stays refused.</exception>
    public async Task<BreakGlassFinish> FinishAsync(
        HttpContext context, UserDto user, SessionStamp stamp, IPAddress? source, CancellationToken ct)
    {
        if (!await issuer.SignInAsync(context, user, stamp, new SignInAudit(AuditAction.LoginSucceeded, "Password + TOTP"), ct))
        {
            return new BreakGlassFinish(false, false);
        }

        // The authorization comes first, so a lift never stands without a row. Nothing after the
        // session is cancellable: a row that commits after a cancelled wait would authorize a lift
        // that never ran, and a lift must always be settled.
        await audit.WriteAsync(Recovery(user, source), CancellationToken.None);
        if (source is null)
        {
            return new BreakGlassFinish(true, false);
        }

        var lift = await GuardedLiftAsync(source);
        if (!lift.Lifted)
        {
            await ReleaseUnusedGuardAsync(lift);
            return new BreakGlassFinish(true, false);
        }

        try
        {
            await audit.WriteAsync(Lift(user, source), CancellationToken.None);
        }
        catch (AuditWriteFailedException)
        {
            if (!await lift.TryRestoreAsync())
            {
                throw new RecoveryIncompleteException(lift.Guard);
            }

            throw;
        }

        await ReleaseAsync(lift);
        return new BreakGlassFinish(true, true);
    }

    private async Task<IRecoveryLift> GuardedLiftAsync(IPAddress source)
    {
        try
        {
            return await blacklist.LiftForRecoveryAsync(source);
        }
        catch (Exception)
        {
            throw new RecoveryIncompleteException();
        }
    }

    // Nothing was removed, so the guard protects nothing: it gets a second try. If that fails too, the
    // guard stays and so does the refusal, which the answer must then admit.
    private static async Task ReleaseUnusedGuardAsync(IRecoveryLift lift)
    {
        try
        {
            await lift.ReleaseAsync();
        }
        catch (Exception)
        {
            await ReleaseAsync(lift);
        }
    }

    private static async Task ReleaseAsync(IRecoveryLift lift)
    {
        try
        {
            await lift.ReleaseAsync();
        }
        catch (Exception)
        {
            throw new RecoveryIncompleteException(lift.Guard);
        }
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
