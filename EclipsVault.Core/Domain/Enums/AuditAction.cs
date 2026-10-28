namespace EclipsVault.Core.Domain.Enums;

/// <summary>Every action the immutable audit trail can record.</summary>
public enum AuditAction
{
    SecretCreated = 1,
    SecretMetadataViewed = 2,
    SecretRevealed = 3,
    SecretUpdated = 4,
    SecretDeleted = 5,
    SecretShredded = 6,
    SecretRotated = 7,
    SecretVersionRevealed = 8,
    SecretVersionRestored = 9,
    SecretShared = 14,
    SecretShareRevoked = 15,
    LoginFailed = 10,
    LoginSucceeded = 11,
    TotpFailed = 12,
    TotpEnrolled = 13,
    HoneyTokenTripped = 20,
    SessionRevoked = 21,
    UserCreated = 30,
    UserDeleted = 31,
    UserTotpReset = 32,
    AccountLockedOut = 33,
    AccountUnlocked = 34,
    TrustedNetworkAdded = 40,
    TrustedNetworkRemoved = 41,
    IpRangeUnblocked = 42,
    BreakGlassRecovery = 43,

    // Self-service profile actions.
    ProfileUpdated = 50,
    AvatarUpdated = 51,
    AvatarRemoved = 52,
    PasswordChanged = 53,
    SelfMfaReset = 54,
    SessionsRevokedSelf = 55,
    PasskeyRegistered = 56,
    PasskeyRemoved = 57,
    PasskeyLogin = 58,

    // Administrative account actions.
    UserRoleChanged = 60,
    UserDisabled = 61,
    UserEnabled = 62,
    UserForceLoggedOut = 63,

    /// <summary>Self-service revocation of one of your own active sessions ("signed-in devices").</summary>
    SessionRevokedByUser = 64,

    // Service accounts & API keys.
    ServiceAccountCreated = 70,
    ServiceAccountDeleted = 71,
    ServiceAccountDisabled = 72,
    ServiceAccountEnabled = 73,
    ApiKeyIssued = 74,
    ApiKeyRevoked = 75,

    // Self-service access requests.
    AccessRequested = 80,
    AccessRequestApproved = 81,
    AccessRequestRejected = 82,
    AccessRequestCancelled = 83,

    // Key lifecycle.
    KekRotated = 90,

    // MFA recovery ("backup") codes.
    RecoveryCodesGenerated = 100,
    RecoveryCodeUsed = 101,

    // Audit attestation (signed checkpoints / export).
    AuditCheckpointCreated = 110,
    AuditBundleExported = 111,

    /// <summary>A user downloaded a copy of their own account data (personal-data export).</summary>
    PersonalDataExported = 112,

    // Step-up re-authentication for sensitive reveals.
    StepUpVerified = 120,
    StepUpFailed = 121,

    // Dynamic secrets: credentials minted on a backend and destroyed when their lease ends.
    DynamicCredentialIssued = 130,
    DynamicCredentialRevoked = 131,
    DynamicCredentialExpired = 132,

    /// <summary>The backend refused to destroy a credential — it may still be live. Critical.</summary>
    DynamicCredentialRevocationFailed = 133,

    /// <summary>The vault changed a real upstream credential and stored the new value.</summary>
    SecretUpstreamRotated = 134,

    /// <summary>
    /// An upstream rotation left the stored value and the live credential out of step, and the
    /// vault could not put it back. Someone has to reconcile it by hand. Critical.
    /// </summary>
    SecretUpstreamRotationDrifted = 135,

    /// <summary>An identity provider's assertion was matched to a vault account.</summary>
    SsoIdentityLinked = 140,

    /// <summary>
    /// The vault turned an SSO sign-in away. Routine when someone simply has no account here — and
    /// the single most useful row in the trail when it isn't, because it is the IdP being told no.
    /// </summary>
    SsoSignInRefused = 141,

    /// <summary>
    /// The account's owner proved an existing credential for one prepared Profile change. Not a sign-in:
    /// it is kept out of sign-in history, so it never makes a source address look familiar.
    /// </summary>
    AccountVerified = 150,

    /// <summary>A wrong proof for a prepared Profile change. It counts towards the account's lockout.</summary>
    AccountVerificationFailed = 151,

    /// <summary>
    /// The vault started without a valid license outside Development — a soft, one-per-startup marker
    /// so an operator has a dated record. It never restricts the vault and is not a security event.
    /// </summary>
    LicenseInvalidProductionUse = 200,

    /// <summary>
    /// A Max-only feature was exercised on a vault whose license does not grant it (a Community/
    /// unlicensed deployment, or a feature switched on beyond the current tier). Soft and
    /// deduplicated — a licensing reminder, never a restriction and never a security event.
    /// </summary>
    LicenseFeatureUnlicensed = 201,

    /// <summary>
    /// A gated Max-only feature was refused because the license does not grant it and its evaluation
    /// period has ended. Distinct from <see cref="LicenseFeatureUnlicensed"/>: the vault actually
    /// refused the action (surfaced as a 402), it did not merely note the use. Non-critical — a
    /// licensing refusal is not a security event.
    /// </summary>
    LicenseFeatureBlocked = 202,

    /// <summary>
    /// A Licensed Capability was exercised for the first time on an unlicensed vault, opening the
    /// evaluation period the licence grants. Dated evidence of when the window opened, for the
    /// operator and their auditor — the capability was allowed, not refused.
    /// </summary>
    LicenseEvaluationStarted = 203
}
