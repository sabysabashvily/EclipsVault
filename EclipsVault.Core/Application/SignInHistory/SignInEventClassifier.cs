using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.SignInHistory;

/// <summary>
/// The single, pure mapping from a raw <see cref="AuditAction"/> to how it reads in the user's
/// sign-in history. Only authentication-shaped actions are recognised; everything else returns
/// null and is excluded from the timeline. <see cref="RelevantActions"/> is the exact set the
/// reader filters on, so the DB query and the classifier can never drift apart.
/// </summary>
public static class SignInEventClassifier
{
    /// <summary>
    /// The audit actions that make up the sign-in history — the authentication attempts and the
    /// account-lock lifecycle. Kept in lock-step with <see cref="Classify"/>: every action here
    /// classifies to a non-null descriptor, and nothing outside it does.
    /// </summary>
    public static readonly IReadOnlyList<AuditAction> RelevantActions = new[]
    {
        AuditAction.LoginSucceeded,
        AuditAction.LoginFailed,
        AuditAction.TotpFailed,
        AuditAction.SignInRefused,
        AuditAction.PasskeyLogin,
        AuditAction.RecoveryCodeUsed,
        AuditAction.StepUpVerified,
        AuditAction.StepUpFailed,
        AuditAction.AccountLockedOut,
        AuditAction.AccountUnlocked
    };

    /// <summary>Classifies an action, or returns null when it is not a sign-in-related event.</summary>
    public static SignInDescriptor? Classify(AuditAction action) => action switch
    {
        AuditAction.LoginSucceeded => new(SignInOutcome.Success, SignInMethod.Password, "Signed in"),
        AuditAction.PasskeyLogin => new(SignInOutcome.Success, SignInMethod.Passkey, "Signed in with a passkey"),
        // The code was spent. That is not a session: LoginSucceeded is the sign-in.
        AuditAction.RecoveryCodeUsed => new(SignInOutcome.Info, SignInMethod.RecoveryCode, "Used a recovery code"),
        AuditAction.SignInRefused => new(SignInOutcome.Failed, SignInMethod.System, "Sign-in refused"),
        AuditAction.StepUpVerified => new(SignInOutcome.Success, SignInMethod.StepUp, "Re-authenticated for a sensitive action"),

        AuditAction.LoginFailed => new(SignInOutcome.Failed, SignInMethod.Password, "Sign-in attempt failed"),
        AuditAction.TotpFailed => new(SignInOutcome.Failed, SignInMethod.TwoFactor, "Two-factor code rejected"),
        AuditAction.StepUpFailed => new(SignInOutcome.Failed, SignInMethod.StepUp, "Step-up verification failed"),

        AuditAction.AccountLockedOut => new(SignInOutcome.Blocked, SignInMethod.System, "Account locked after repeated failures"),
        AuditAction.AccountUnlocked => new(SignInOutcome.Info, SignInMethod.System, "Account unlocked"),

        _ => null
    };
}
