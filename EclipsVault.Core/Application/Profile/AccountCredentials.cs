using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Profile;

/// <summary>
/// The credentials a vault account can verify itself with, and which security changes they allow.
/// A recovery code stands in for the authenticator, so it works only while one is enrolled.
/// </summary>
public sealed record AccountCredentials(bool AuthenticatorEnrolled, int UnusedRecoveryCodes, IReadOnlyList<Guid> PasskeyIds)
{
    /// <summary>
    /// The proof methods that can verify <paramref name="kind"/>. A passkey being removed cannot verify
    /// its own removal.
    /// </summary>
    public IReadOnlyList<AccountProofMethod> MethodsFor(ProfileOperationKind kind, Guid? targetPasskeyId)
    {
        var methods = new List<AccountProofMethod>();
        if (AuthenticatorEnrolled)
        {
            methods.Add(AccountProofMethod.PasswordAndAuthenticatorCode);
        }

        if (AuthenticatorEnrolled && UnusedRecoveryCodes > 0)
        {
            methods.Add(AccountProofMethod.PasswordAndRecoveryCode);
        }

        var removed = kind == ProfileOperationKind.RemovePasskey ? targetPasskeyId : null;
        if (PasskeyIds.Any(id => id != removed))
        {
            methods.Add(AccountProofMethod.Passkey);
        }

        return methods;
    }

    /// <summary>Why <paramref name="kind"/> cannot be prepared for this account, or null when it can.</summary>
    public ProfileRefusal? RefusalFor(ProfileOperationKind kind, Guid? targetPasskeyId)
    {
        if (kind is ProfileOperationKind.ResetAuthenticator or ProfileOperationKind.ReplaceRecoveryCodes && !AuthenticatorEnrolled)
        {
            return ProfileRefusal.AuthenticatorNotEnrolled;
        }

        if (kind == ProfileOperationKind.RemovePasskey && (targetPasskeyId is not { } target || !PasskeyIds.Contains(target)))
        {
            return ProfileRefusal.PasskeyNotFound;
        }

        // Every method left after the last passkey goes needs the authenticator.
        if (kind == ProfileOperationKind.RemovePasskey && PasskeyIds.Count == 1 && !AuthenticatorEnrolled)
        {
            return ProfileRefusal.AlternateMethodRequired;
        }

        return MethodsFor(kind, targetPasskeyId).Count == 0 ? ProfileRefusal.MethodUnavailable : null;
    }

    /// <summary>
    /// Whether the method that verified removing the account's last passkey still works once it is gone.
    /// A spent recovery code proved the method only if another unused code remains.
    /// </summary>
    public bool KeepsWorkingWithoutPasskeys(AccountProofMethod provenWith) => provenWith switch
    {
        AccountProofMethod.PasswordAndAuthenticatorCode => AuthenticatorEnrolled,
        AccountProofMethod.PasswordAndRecoveryCode => AuthenticatorEnrolled && UnusedRecoveryCodes > 0,
        _ => false
    };
}
