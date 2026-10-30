using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Passkeys;

/// <summary>
/// The output of a "begin" ceremony: <see cref="OptionsJson"/> is the JSON handed to the
/// browser (fed to <c>navigator.credentials</c>), while <see cref="Challenge"/> (base64url)
/// is stashed server-side and handed back to the matching "complete" call so the response
/// can be verified against the exact challenge that was issued. A sign-in that named an account also
/// carries <see cref="NamedAccount"/>, stashed with the challenge for the same reason.
/// </summary>
public sealed record PasskeyCeremonyOptions(string OptionsJson, string Challenge, PasskeyNamedAccount? NamedAccount = null);

/// <summary>
/// The account a passkey sign-in named, and its credential generation when the challenge was issued.
/// Only that account's passkeys can finish the sign-in, and only while that generation is current.
/// </summary>
public sealed record PasskeyNamedAccount(Guid AccountId, SessionStamp Stamp);

/// <summary>Outcome of a WebAuthn registration ceremony, with the credential generation a success committed.</summary>
public sealed record PasskeyRegistrationResult(bool Success, string? Error, long? CredentialGeneration)
{
    public static PasskeyRegistrationResult Registered(long credentialGeneration) => new(true, null, credentialGeneration);

    public static PasskeyRegistrationResult Failed(string error) => new(false, error, null);
}

/// <summary>
/// Outcome of a WebAuthn assertion (passwordless sign-in) ceremony. On success the resolved
/// user and the stamp to issue its session under are returned, so the Web layer can issue the full
/// session exactly as it does after TOTP.
/// </summary>
public sealed record PasskeyAssertionResult(bool Success, string? Error, UserDto? User, SessionStamp? Stamp)
{
    public static PasskeyAssertionResult Failed(string error) => new(false, error, null, null);

    public static PasskeyAssertionResult Succeeded(UserDto user, SessionStamp stamp) => new(true, null, user, stamp);
}

/// <summary>A registered passkey as shown in the self-service profile list.</summary>
public sealed record PasskeySummary(Guid Id, string Nickname, DateTimeOffset CreatedAtUtc);

/// <summary>How a passkey answered an account-verification challenge. <see cref="Rejection"/> is safe to audit.</summary>
public sealed record PasskeyProof(PasskeyProofOutcome Outcome, string? Rejection = null)
{
    public static readonly PasskeyProof Verified = new(PasskeyProofOutcome.Verified);

    public static readonly PasskeyProof Malformed = new(PasskeyProofOutcome.Malformed);

    public static PasskeyProof Rejected(string reason) => new(PasskeyProofOutcome.Rejected, reason);
}

/// <summary>A verified registration response: the credential to store, not yet saved, or why it was refused.</summary>
public sealed record PasskeyAttestation(PasskeyCredential? Credential, string? Error)
{
    public static PasskeyAttestation Failed(string error) => new(null, error);
}
