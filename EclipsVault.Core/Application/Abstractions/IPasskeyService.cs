namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Drives the WebAuthn/passkey ceremonies for passwordless multi-factor sign-in. The
/// implementation lives in Infrastructure (the FIDO2 relying-party logic) so the domain
/// layer stays free of any crypto or wire-format concern; it traffics only in JSON strings
/// and Core DTOs. Ceremony state (the issued challenge) is carried by the Web layer between
/// each "begin"/"complete" pair, keeping this service stateless.
/// </summary>
public interface IPasskeyService
{
    /// <summary>Issues creation options for the signed-in user to register a new authenticator.</summary>
    Task<PasskeyCeremonyOptions> BeginRegistrationAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Verifies the authenticator's attestation response and stores the new credential, advancing the
    /// credential generation. Fails when the account's generation is no longer
    /// <paramref name="credentialGeneration"/>, the one the ceremony began under.
    /// </summary>
    Task<PasskeyRegistrationResult> CompleteRegistrationAsync(
        Guid userId, long credentialGeneration, string expectedChallenge, string responseJson, string? nickname, CancellationToken ct);

    /// <summary>Issues assertion options for a passwordless sign-in. A username scopes the allowed credentials; null enables discoverable credentials.</summary>
    Task<PasskeyCeremonyOptions> BeginAssertionAsync(string? usernameOrEmail, CancellationToken ct);

    /// <summary>
    /// Verifies an assertion response and, on success, returns the authenticated user. Without a named
    /// account the credential identifies its owner, whose generation is taken at that point, before
    /// the assertion is verified.
    /// </summary>
    Task<PasskeyAssertionResult> CompleteAssertionAsync(
        string expectedChallenge, PasskeyNamedAccount? namedAccount, string responseJson, CancellationToken ct);

    /// <summary>Lists the caller's registered passkeys for the profile page.</summary>
    Task<IReadOnlyList<PasskeySummary>> ListForUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Removes one of the caller's own passkeys, advancing the credential generation.</summary>
    /// <returns>The credential generation the removal committed, or null when the passkey was not found.</returns>
    /// <exception cref="Domain.Exceptions.StaleProofException">The account's credentials changed after the session was issued.</exception>
    Task<long?> RemoveAsync(Guid userId, long credentialGeneration, Guid passkeyId, CancellationToken ct);
}
