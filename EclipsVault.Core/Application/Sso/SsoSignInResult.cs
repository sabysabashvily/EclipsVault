using EclipsVault.Core.Application.Authentication;

namespace EclipsVault.Core.Application.Sso;

/// <summary>
/// The vault's answer to an IdP's assertion.
///
/// <see cref="Outcome"/> and <see cref="User"/> answer "may this person in at all"; the separate
/// <see cref="SecondFactorSatisfied"/> answers "are they done authenticating". Keeping them apart is
/// the point — a linked identity that has only proved one factor is not a session, and collapsing
/// the two questions into one boolean is how a factor goes missing.
///
/// A linked identity also carries the <see cref="Stamp"/> its session, or its pending second factor,
/// must be issued under.
/// </summary>
public sealed record SsoSignInResult(SsoOutcome Outcome, UserDto? User, SessionStamp? Stamp, bool SecondFactorSatisfied)
{
    public static SsoSignInResult Refused(SsoOutcome outcome) => new(outcome, null, null, false);

    public static SsoSignInResult Linked(UserDto user, SessionStamp stamp, bool secondFactorSatisfied) =>
        new(SsoOutcome.Linked, user, stamp, secondFactorSatisfied);
}
