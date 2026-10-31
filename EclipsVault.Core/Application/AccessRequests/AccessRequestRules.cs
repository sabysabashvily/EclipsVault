using EclipsVault.Core.Application.Abac;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.AccessRequests;

/// <summary>
/// Whether a share could help an account open a secret (ADR-0027), which decides both filing and approving
/// a request. The question goes to <see cref="SecretAccessPolicy"/> itself rather than to a copy of its
/// rules: with and without a share, on a trusted network inside the production window.
/// </summary>
public static class AccessRequestRules
{
    // Network and time change by the hour, so neither decides whether a share could ever help.
    private static readonly RequestContext AnyHourOnATrustedNetwork =
        new(DateTimeOffset.MinValue, SourceIp: null, IsWithinProductionWindow: true, IsFromTrustedNetwork: true);

    /// <summary>The reason a share could not help this account open the secret, or null when it could.</summary>
    public static AccessRequestRefusal? ShareRefusal(SubjectAttributes account, ResourceAttributes secret, bool holdsActiveShare)
    {
        var withShare = AnyHourOnATrustedNetwork with { IsExplicitlyGranted = true };
        if (!SecretAccessPolicy.Evaluate(account, secret, withShare).IsAllowed)
        {
            return AccessRequestRefusal.ShareCannotHelp;
        }

        if (holdsActiveShare || SecretAccessPolicy.Evaluate(account, secret, AnyHourOnATrustedNetwork).IsAllowed)
        {
            return AccessRequestRefusal.AlreadyAbleToOpen;
        }

        return null;
    }

    public static string ExplainToRequester(AccessRequestRefusal refusal) => refusal switch
    {
        AccessRequestRefusal.ShareCannotHelp => "A share would not help: your clearance is below this secret's sensitivity.",
        AccessRequestRefusal.AlreadyAbleToOpen => "A share would not help: your project, clearance or an active share already covers this secret.",
        AccessRequestRefusal.AlreadyPending => "You already have a pending request for this secret.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null),
    };

    public static string ExplainToReviewer(AccessRequestRefusal refusal) => refusal switch
    {
        AccessRequestRefusal.ShareCannotHelp => "A share would not help: the requester's clearance is below this secret's sensitivity.",
        AccessRequestRefusal.AlreadyAbleToOpen => "A share would not help: the requester's project, clearance or an active share already covers this secret.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Approval re-checks only whether a share could help."),
    };
}
