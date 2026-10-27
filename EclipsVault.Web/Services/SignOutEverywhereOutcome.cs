namespace EclipsVault.Web.Services;

/// <summary>What signing out everywhere achieved, so the account is told the truth about its other devices.</summary>
public enum SignOutEverywhereOutcome
{
    /// <summary>Every session was revoked and the sign-out recorded.</summary>
    SignedOut = 0,

    /// <summary>Every session was revoked, but the audit trail could not record it.</summary>
    RevokedUnrecorded = 1,

    /// <summary>The session store did not confirm the revocation; other devices may still be signed in.</summary>
    Incomplete = 2
}
