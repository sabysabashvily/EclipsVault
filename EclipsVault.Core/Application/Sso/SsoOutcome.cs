namespace EclipsVault.Core.Application.Sso;

/// <summary>Why an SSO sign-in was allowed, or why it was not.</summary>
public enum SsoOutcome
{
    /// <summary>Matched a vault account. Whether a second factor is still owed is a separate question.</summary>
    Linked = 1,

    /// <summary>The IdP sent no email, so there is nothing to match a vault account on.</summary>
    NoEmail = 2,

    /// <summary>The IdP did not vouch that the address is theirs. Linking on it would be takeover.</summary>
    EmailNotVerified = 3,

    /// <summary>A real person at the IdP with no account here. The IdP does not decide who may in.</summary>
    NoVaultAccount = 4,

    /// <summary>The account exists but is disabled here — the vault's answer outranks the IdP's.</summary>
    Disabled = 5,

    /// <summary>The account is awaiting activation by invitation and cannot sign in by any route.</summary>
    NotActivated = 6
}
