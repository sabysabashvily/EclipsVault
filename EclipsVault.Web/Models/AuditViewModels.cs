using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Models;

public sealed class AuditIndexViewModel
{
    public IReadOnlyList<AuditEntryDto> Entries { get; init; } = [];

    /// <summary>Set only after the admin runs "Verify integrity"; null on a plain page load.</summary>
    public AuditIntegrityReport? Integrity { get; init; }

    /// <summary>The most recent signed checkpoint, or null if none has been created.</summary>
    public AuditCheckpointDto? LatestCheckpoint { get; init; }

    /// <summary>Identifier of the active audit signing key.</summary>
    public string? SigningKeyId { get; init; }
}

/// <summary>Presentation mapping for audit actions: human label + badge tone.</summary>
public static class AuditDisplay
{
    public static (string Label, string Tone) For(AuditAction action) => action switch
    {
        AuditAction.SecretCreated => ("Created", "ok"),
        AuditAction.SecretMetadataViewed => ("Viewed", "muted"),
        AuditAction.SecretRevealed => ("Revealed", "warn"),
        AuditAction.SecretUpdated => ("Updated", "muted"),
        AuditAction.SecretDeleted => ("Deleted", "danger"),
        AuditAction.SecretShredded => ("Shredded", "danger"),
        AuditAction.SecretRotated => ("Rotated", "ok"),
        AuditAction.SecretVersionRevealed => ("Version revealed", "warn"),
        AuditAction.SecretVersionRestored => ("Version restored", "warn"),
        AuditAction.SecretShared => ("Shared", "ok"),
        AuditAction.SecretShareRevoked => ("Share revoked", "warn"),
        AuditAction.LoginFailed => ("Login failed", "warn"),
        AuditAction.LoginSucceeded => ("Signed in", "ok"),
        AuditAction.TotpFailed => ("TOTP failed", "warn"),
        AuditAction.TotpEnrolled => ("TOTP enrolled", "ok"),
        AuditAction.HoneyTokenTripped => ("Honey token", "critical"),
        AuditAction.SessionRevoked => ("Session revoked", "danger"),
        AuditAction.UserCreated => ("User created", "ok"),
        AuditAction.UserDeleted => ("User deleted", "danger"),
        AuditAction.UserTotpReset => ("MFA reset", "warn"),
        AuditAction.AccountLockedOut => ("Account locked", "danger"),
        AuditAction.AccountUnlocked => ("Account unlocked", "ok"),
        AuditAction.TrustedNetworkAdded => ("Network trusted", "ok"),
        AuditAction.TrustedNetworkRemoved => ("Network removed", "warn"),
        AuditAction.IpRangeUnblocked => ("Range unblocked", "warn"),
        AuditAction.BreakGlassRecovery => ("Break-glass recovery", "warn"),
        AuditAction.ProfileUpdated => ("Profile updated", "muted"),
        AuditAction.AvatarUpdated => ("Avatar updated", "muted"),
        AuditAction.AvatarRemoved => ("Avatar removed", "muted"),
        AuditAction.PasswordChanged => ("Password changed", "ok"),
        AuditAction.SelfMfaReset => ("MFA reset (self)", "warn"),
        AuditAction.SessionsRevokedSelf => ("Signed out everywhere", "muted"),
        AuditAction.SessionRevokedByUser => ("Device signed out", "warn"),
        AuditAction.UserRoleChanged => ("Role changed", "warn"),
        AuditAction.UserDisabled => ("Account disabled", "danger"),
        AuditAction.UserEnabled => ("Account enabled", "ok"),
        AuditAction.UserForceLoggedOut => ("Force logout", "warn"),
        AuditAction.ServiceAccountCreated => ("Service account created", "ok"),
        AuditAction.ServiceAccountDeleted => ("Service account deleted", "danger"),
        AuditAction.ServiceAccountDisabled => ("Service account disabled", "danger"),
        AuditAction.ServiceAccountEnabled => ("Service account enabled", "ok"),
        AuditAction.ApiKeyIssued => ("API key issued", "ok"),
        AuditAction.ApiKeyRevoked => ("API key revoked", "warn"),
        AuditAction.AuditCheckpointCreated => ("Checkpoint signed", "ok"),
        AuditAction.AuditBundleExported => ("Audit exported", "muted"),
        AuditAction.LicenseFeatureBlocked => ("Feature blocked (unlicensed)", "warn"),
        AuditAction.LicenseEvaluationStarted => ("Evaluation started", "muted"),
        AuditAction.PersonalDataExported => ("Data exported", "muted"),
        AuditAction.PasskeyRegistered => ("Passkey added", "ok"),
        AuditAction.PasskeyRemoved => ("Passkey removed", "warn"),
        AuditAction.PasskeyLogin => ("Passkey sign-in", "ok"),
        AuditAction.AccessRequested => ("Access requested", "muted"),
        AuditAction.AccessRequestApproved => ("Access approved", "ok"),
        AuditAction.AccessRequestRejected => ("Access rejected", "warn"),
        AuditAction.AccessRequestCancelled => ("Access cancelled", "muted"),
        AuditAction.KekRotated => ("KEK rotated", "warn"),
        AuditAction.RecoveryCodesGenerated => ("Recovery codes issued", "ok"),
        AuditAction.RecoveryCodeUsed => ("Recovery code used", "warn"),
        AuditAction.StepUpVerified => ("Step-up verified", "ok"),
        AuditAction.StepUpFailed => ("Step-up failed", "warn"),
        AuditAction.AccountVerified => ("Account verified", "ok"),
        AuditAction.AccountVerificationFailed => ("Account verification failed", "warn"),
        _ => (action.ToString(), "muted")
    };

    public static string Ago(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var span = now - timestamp;
        return span switch
        {
            { TotalSeconds: < 60 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)span.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)span.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)span.TotalDays}d ago",
            _ => timestamp.UtcDateTime.ToString("yyyy-MM-dd")
        };
    }
}
