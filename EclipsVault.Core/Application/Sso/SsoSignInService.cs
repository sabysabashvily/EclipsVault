using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Sso;

/// <summary>
/// The vault's half of SSO: match the IdP's assertion to an account here, or refuse it.
///
/// Every path is fail-closed and audited, including the refusals — an IdP vouching for someone the
/// vault turns away is precisely the event a security team wants to see, and it is invisible unless
/// it is recorded here.
/// </summary>
public sealed class SsoSignInService : ISsoSignInService
{
    private readonly IUserRepository _users;
    private readonly IAuditSink _audit;
    private readonly SsoPolicy _policy;

    public SsoSignInService(IUserRepository users, IAuditSink audit, SsoPolicy policy)
    {
        _users = users;
        _audit = audit;
        _policy = policy;
    }

    public async Task<SsoSignInResult> SignInAsync(ExternalIdentity identity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identity.Email))
        {
            return await RefuseAsync(SsoOutcome.NoEmail, identity,
                "the identity provider sent no email address, so there is nothing to match an account on", ct);
        }

        // The link is the email, so an unverified one is not a detail — it is the attack. An IdP that
        // lets a person self-assert an address lets them assert someone else's, and matching on it
        // would hand them that account. Worth a critical row: nobody reaches here by accident.
        if (!identity.EmailVerified)
        {
            return await RefuseAsync(SsoOutcome.EmailNotVerified, identity,
                $"the identity provider did not verify '{identity.Email}'; refusing to match an account to an address nobody vouched for",
                ct, isCritical: true);
        }

        var user = await _users.FindByEmailAsync(identity.Email, ct);
        if (user is null)
        {
            // Real at the IdP, unknown here. The IdP authenticates; it does not decide who may in.
            return await RefuseAsync(SsoOutcome.NoVaultAccount, identity,
                $"'{identity.Email}' has no account in this vault", ct);
        }

        if (user.IsDisabled)
        {
            return await RefuseAsync(SsoOutcome.Disabled, identity,
                $"the account for '{identity.Email}' is disabled here", ct, user.Id, user.Username);
        }

        // A factor is only waived if the IdP says it performed one, AND the operator has said they
        // trust it. Otherwise the vault asks for its own — the cost is one TOTP prompt.
        var secondFactorSatisfied =
            _policy.TrustIdpMultiFactor && IdpMfaPolicy.AssertedMultiFactor(identity.AuthenticationMethods);

        await _audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.SsoIdentityLinked,
            ResourceType = "Sso",
            ResourceId = user.Id,
            ResourceName = user.Username,
            Details = $"Matched '{identity.Email}' from {Describe(identity)}. " +
                      (secondFactorSatisfied
                          ? "The provider asserted multi-factor authentication and this vault is configured to trust it."
                          : "A second factor is still required here."),
            ActorUserId = user.Id,
            ActorUsername = user.Username
        }, ct);

        return SsoSignInResult.Linked(UserDto.From(user), secondFactorSatisfied);
    }

    private async Task<SsoSignInResult> RefuseAsync(
        SsoOutcome outcome,
        ExternalIdentity identity,
        string reason,
        CancellationToken ct,
        Guid? actorUserId = null,
        string? actorUsername = null,
        bool isCritical = false)
    {
        await _audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.SsoSignInRefused,
            ResourceType = "Sso",
            ResourceId = actorUserId,
            ResourceName = identity.Email,
            Details = $"Refused ({outcome}): {reason}. Asserted by {Describe(identity)}.",
            IsCritical = isCritical,
            ActorUserId = actorUserId,
            InheritActorUserId = false,
            // Name the subject the IdP asserted. An unknown subject must not inherit the id of
            // another account whose session the callback request might already carry.
            ActorUsername = actorUsername ?? identity.Email ?? "sso:anonymous"
        }, ct);

        return SsoSignInResult.Refused(outcome);
    }

    private static string Describe(ExternalIdentity identity) =>
        $"issuer '{identity.Issuer ?? "unknown"}' (subject '{identity.Subject ?? "unknown"}')";
}
