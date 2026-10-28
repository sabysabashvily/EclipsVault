using System.Text;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Activity;

/// <summary>
/// The single, pure mapping from a raw <see cref="AuditAction"/> to how it reads in a user's
/// own activity feed: which category it belongs to, a plain-language title, and how much it
/// should stand out. Titles are written from the signed-in user's point of view ("Signed in",
/// "Revealed a secret") because the feed only ever shows the user their own actions.
/// Every enum value is handled; any future value degrades gracefully to a spaced-out title.
/// </summary>
public static class ActivityDescriber
{
    public static ActivityDescription Describe(AuditAction action) => action switch
    {
        // --- Secrets -----------------------------------------------------------------
        AuditAction.SecretCreated => new(ActivityCategory.Secrets, "Created a secret", ActivitySeverity.Routine),
        AuditAction.SecretMetadataViewed => new(ActivityCategory.Secrets, "Viewed a secret's details", ActivitySeverity.Routine),
        AuditAction.SecretRevealed => new(ActivityCategory.Secrets, "Revealed a secret value", ActivitySeverity.Notable),
        AuditAction.SecretUpdated => new(ActivityCategory.Secrets, "Updated a secret", ActivitySeverity.Routine),
        AuditAction.SecretDeleted => new(ActivityCategory.Secrets, "Deleted a secret", ActivitySeverity.Notable),
        AuditAction.SecretShredded => new(ActivityCategory.Secrets, "Shredded a secret", ActivitySeverity.Notable),
        AuditAction.SecretRotated => new(ActivityCategory.Secrets, "Rotated a secret", ActivitySeverity.Routine),
        AuditAction.SecretVersionRevealed => new(ActivityCategory.Secrets, "Revealed a previous secret version", ActivitySeverity.Notable),
        AuditAction.SecretVersionRestored => new(ActivityCategory.Secrets, "Restored a previous secret version", ActivitySeverity.Notable),

        // --- Sharing & access requests ----------------------------------------------
        AuditAction.SecretShared => new(ActivityCategory.Sharing, "Shared a secret", ActivitySeverity.Notable),
        AuditAction.SecretShareRevoked => new(ActivityCategory.Sharing, "Revoked a secret share", ActivitySeverity.Notable),
        AuditAction.AccessRequested => new(ActivityCategory.Sharing, "Requested access to a secret", ActivitySeverity.Routine),
        AuditAction.AccessRequestApproved => new(ActivityCategory.Sharing, "Approved an access request", ActivitySeverity.Notable),
        AuditAction.AccessRequestRejected => new(ActivityCategory.Sharing, "Rejected an access request", ActivitySeverity.Notable),
        AuditAction.AccessRequestCancelled => new(ActivityCategory.Sharing, "Cancelled an access request", ActivitySeverity.Routine),

        // --- Authentication ----------------------------------------------------------
        AuditAction.LoginSucceeded => new(ActivityCategory.Authentication, "Signed in", ActivitySeverity.Routine),
        AuditAction.LoginFailed => new(ActivityCategory.Authentication, "A sign-in attempt failed", ActivitySeverity.Notable),
        AuditAction.TotpFailed => new(ActivityCategory.Authentication, "A two-factor code was rejected", ActivitySeverity.Notable),
        AuditAction.PasskeyLogin => new(ActivityCategory.Authentication, "Signed in with a passkey", ActivitySeverity.Routine),
        AuditAction.RecoveryCodeUsed => new(ActivityCategory.Authentication, "Signed in with a recovery code", ActivitySeverity.Notable),

        // --- Account & self-service --------------------------------------------------
        AuditAction.TotpEnrolled => new(ActivityCategory.Account, "Set up your authenticator", ActivitySeverity.Notable),
        AuditAction.ProfileUpdated => new(ActivityCategory.Account, "Updated your profile", ActivitySeverity.Routine),
        AuditAction.AvatarUpdated => new(ActivityCategory.Account, "Changed your profile picture", ActivitySeverity.Routine),
        AuditAction.AvatarRemoved => new(ActivityCategory.Account, "Removed your profile picture", ActivitySeverity.Routine),
        AuditAction.PasswordChanged => new(ActivityCategory.Account, "Changed your password", ActivitySeverity.Notable),
        AuditAction.SelfMfaReset => new(ActivityCategory.Account, "Reset your authenticator", ActivitySeverity.Notable),
        AuditAction.PasskeyRegistered => new(ActivityCategory.Account, "Added a passkey", ActivitySeverity.Notable),
        AuditAction.PasskeyRemoved => new(ActivityCategory.Account, "Removed a passkey", ActivitySeverity.Notable),
        AuditAction.RecoveryCodesGenerated => new(ActivityCategory.Account, "Generated new recovery codes", ActivitySeverity.Notable),
        AuditAction.PersonalDataExported => new(ActivityCategory.Account, "Downloaded a copy of your data", ActivitySeverity.Notable),

        // --- Security events (high signal) ------------------------------------------
        AuditAction.SessionsRevokedSelf => new(ActivityCategory.Security, "Signed out of all sessions", ActivitySeverity.Notable),
        AuditAction.SessionRevokedByUser => new(ActivityCategory.Security, "Signed out one of your devices", ActivitySeverity.Notable),
        AuditAction.StepUpVerified => new(ActivityCategory.Security, "Passed step-up verification", ActivitySeverity.Routine),
        AuditAction.StepUpFailed => new(ActivityCategory.Security, "Step-up verification failed", ActivitySeverity.Notable),
        AuditAction.AccountVerified => new(ActivityCategory.Security, "Verified your account for a security change", ActivitySeverity.Routine),
        AuditAction.AccountVerificationFailed => new(ActivityCategory.Security, "Account verification failed", ActivitySeverity.Notable),
        AuditAction.AccountUnlocked => new(ActivityCategory.Security, "Your account was unlocked", ActivitySeverity.Notable),
        AuditAction.AccountLockedOut => new(ActivityCategory.Security, "Your account was locked after failed sign-ins", ActivitySeverity.Critical),
        AuditAction.SessionRevoked => new(ActivityCategory.Security, "A session was force-revoked", ActivitySeverity.Critical),
        AuditAction.HoneyTokenTripped => new(ActivityCategory.Security, "A honey-token tripwire was triggered", ActivitySeverity.Critical),
        AuditAction.BreakGlassRecovery => new(ActivityCategory.Security, "Performed break-glass recovery", ActivitySeverity.Critical),

        // --- Administration ----------------------------------------------------------
        AuditAction.UserCreated => new(ActivityCategory.Administration, "Created a user", ActivitySeverity.Notable),
        AuditAction.UserDeleted => new(ActivityCategory.Administration, "Deleted a user", ActivitySeverity.Notable),
        AuditAction.UserTotpReset => new(ActivityCategory.Administration, "Reset a user's MFA", ActivitySeverity.Notable),
        AuditAction.UserRoleChanged => new(ActivityCategory.Administration, "Changed a user's role", ActivitySeverity.Notable),
        AuditAction.UserDisabled => new(ActivityCategory.Administration, "Disabled a user", ActivitySeverity.Notable),
        AuditAction.UserEnabled => new(ActivityCategory.Administration, "Enabled a user", ActivitySeverity.Notable),
        AuditAction.UserForceLoggedOut => new(ActivityCategory.Administration, "Forced a user to sign out", ActivitySeverity.Notable),
        AuditAction.TrustedNetworkAdded => new(ActivityCategory.Administration, "Added a trusted network", ActivitySeverity.Notable),
        AuditAction.TrustedNetworkRemoved => new(ActivityCategory.Administration, "Removed a trusted network", ActivitySeverity.Notable),
        AuditAction.IpRangeUnblocked => new(ActivityCategory.Administration, "Unblocked an IP range", ActivitySeverity.Notable),
        AuditAction.ServiceAccountCreated => new(ActivityCategory.Administration, "Created a service account", ActivitySeverity.Notable),
        AuditAction.ServiceAccountDeleted => new(ActivityCategory.Administration, "Deleted a service account", ActivitySeverity.Notable),
        AuditAction.ServiceAccountDisabled => new(ActivityCategory.Administration, "Disabled a service account", ActivitySeverity.Notable),
        AuditAction.ServiceAccountEnabled => new(ActivityCategory.Administration, "Enabled a service account", ActivitySeverity.Notable),
        AuditAction.ApiKeyIssued => new(ActivityCategory.Administration, "Issued an API key", ActivitySeverity.Notable),
        AuditAction.ApiKeyRevoked => new(ActivityCategory.Administration, "Revoked an API key", ActivitySeverity.Notable),
        AuditAction.KekRotated => new(ActivityCategory.Administration, "Rotated the key-encryption key", ActivitySeverity.Critical),
        AuditAction.AuditCheckpointCreated => new(ActivityCategory.Administration, "Signed an audit checkpoint", ActivitySeverity.Notable),
        AuditAction.AuditBundleExported => new(ActivityCategory.Administration, "Exported the audit trail", ActivitySeverity.Notable),

        // --- Licensing (soft — an operational notice, never a security event) --------
        AuditAction.LicenseInvalidProductionUse => new(ActivityCategory.Administration, "Started unlicensed in production", ActivitySeverity.Notable),
        AuditAction.LicenseFeatureUnlicensed => new(ActivityCategory.Administration, "Used a premium feature without a license", ActivitySeverity.Notable),
        AuditAction.LicenseFeatureBlocked => new(ActivityCategory.Administration, "Refused an unlicensed premium feature", ActivitySeverity.Notable),
        AuditAction.LicenseEvaluationStarted => new(ActivityCategory.Administration, "Started a premium feature's evaluation period", ActivitySeverity.Routine),

        // Any action added in future still renders as a readable, spaced-out title.
        _ => new(ActivityCategory.Other, Humanize(action), ActivitySeverity.Routine)
    };

    /// <summary>Turns an unmapped enum name ("SomeNewAction") into spaced words ("Some new action").</summary>
    private static string Humanize(AuditAction action)
    {
        var name = action.ToString();
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c))
            {
                sb.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
