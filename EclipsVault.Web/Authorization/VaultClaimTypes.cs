namespace EclipsVault.Web.Authorization;

public static class VaultClaimTypes
{
    public const string Clearance = "vault:clearance";
    public const string Project = "vault:project";
    public const string AuthTime = "vault:auth_time";

    /// <summary>Unix-seconds timestamp of the last step-up re-authentication; refreshes the strong-auth clock for sensitive reveals.</summary>
    public const string StepUpTime = "vault:stepup_time";

    /// <summary>User-editable display name shown in the UI. The immutable login username stays in ClaimTypes.Name (the audit anchor).</summary>
    public const string Display = "vault:display";

    /// <summary>Cache-busting version for the current user's avatar URL; bumped on sign-in and whenever the avatar changes.</summary>
    public const string AvatarVersion = "vault:avatar_v";

    /// <summary>Distinguishes an interactive user from a "service" account principal.</summary>
    public const string ActorType = "vault:actor_type";

    /// <summary>API-key scope: restricts the key to a single project (present only for scoped keys).</summary>
    public const string ScopeProject = "vault:scope_project";

    /// <summary>API-key scope: "true" when the key may read metadata but never a secret value.</summary>
    public const string ScopeMetadataOnly = "vault:scope_metadata_only";

    /// <summary>Unique id of this interactive session, so a single "signed-in device" can be revoked on its own.</summary>
    public const string SessionId = "vault:sid";

    /// <summary>The account's credential generation the session or pending sign-in was issued under.</summary>
    public const string CredentialGeneration = "vault:cg";

    /// <summary>The account's session revocation version the session or pending sign-in was issued under.</summary>
    public const string RevocationVersion = "vault:rv";
}

/// <summary>Shared interactive-session settings, so the cookie lifetime has one source of truth.</summary>
public static class SessionDefaults
{
    /// <summary>How long an interactive session cookie (and its registry record) lives.</summary>
    public static readonly TimeSpan InteractiveLifetime = TimeSpan.FromHours(9);
}

public static class AuthSchemes
{
    /// <summary>Short-lived scheme issued after the password factor, before TOTP.</summary>
    public const string MfaPending = "EclipsVault.MfaPending";

    /// <summary>Bearer / X-Api-Key authentication for non-interactive service accounts.</summary>
    public const string ApiKey = "ApiKey";

    /// <summary>
    /// The OpenID Connect handler. Deliberately not an application scheme: signing in here proves
    /// only what the identity provider asserts, and the vault still has to decide whether that
    /// person may in at all. The principal it produces is read once, at the callback, and discarded.
    /// </summary>
    public const string Oidc = "EclipsVault.Oidc";

    /// <summary>Holds the OIDC handler's correlation and nonce for the round trip to the IdP.</summary>
    public const string OidcCorrelation = "EclipsVault.OidcCorrelation";
}

public static class VaultPolicies
{
    /// <summary>The session requirement shared by interactive pages and the UI Area list.</summary>
    public const string Session = "Session";

    public const string SecretAccess = "SecretAccess";

    /// <summary>Requires TopSecret clearance; gates the administration area.</summary>
    public const string AdminOnly = "AdminOnly";
}
