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
    AuthenticatorNotEnrolled
}
