namespace EclipsVault.Core.Domain.Enums;

/// <summary>Why a prepared Profile operation could not be prepared, verified or committed.</summary>
public enum ProfileRefusal
{
    /// <summary>The input is malformed or breaks a rule, such as the password policy.</summary>
    InvalidInput = 1,

    /// <summary>The proof was wrong. It counted towards the account's lockout.</summary>
    VerificationFailed,

    /// <summary>The account is locked out, or too many changes are pending at once.</summary>
    Throttled,

    /// <summary>No operation with this id belongs to this session.</summary>
    OperationNotFound,

    /// <summary>No passkey with this id belongs to this account.</summary>
    PasskeyNotFound,

    /// <summary>The operation was not verified in time.</summary>
    OperationExpired,

    /// <summary>The account's credentials, or the operation's target, changed after it was prepared.</summary>
    OperationChanged,

    /// <summary>The passkey challenge expired, was replaced or was already answered.</summary>
    CeremonyExpired,

    /// <summary>The operation has not been verified yet.</summary>
    VerificationRequired,

    /// <summary>The verification is older than its five minutes.</summary>
    VerificationExpired,

    /// <summary>The account cannot prove itself with the requested method, or with any method.</summary>
    MethodUnavailable,

    /// <summary>Removing the last passkey needs another method that keeps working afterwards.</summary>
    AlternateMethodRequired,

    /// <summary>The change needs an enrolled authenticator.</summary>
    AuthenticatorNotEnrolled,

    /// <summary>Email changes need configured, enabled SMTP, or the delivery could not be confirmed.</summary>
    MailUnavailable,

    /// <summary>The address is already this account's email or another account's, so it cannot be used.</summary>
    EmailConflict,

    /// <summary>The mailbox code does not match the one delivered for this change.</summary>
    EmailCodeInvalid,

    /// <summary>The mailbox code expired or was retired after too many mistakes; start the change again.</summary>
    EmailCodeExpired,

    /// <summary>The code was sent, but its delivery could not be committed after the session-store recheck.</summary>
    EmailRequestIncomplete
}
