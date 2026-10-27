namespace EclipsVault.Core.Application.Mfa;

/// <summary>
/// Self-service management of single-use MFA recovery codes (NIST SP 800-63B "look-up
/// secrets"). Generation returns the plaintext exactly once; only salted Argon2id hashes
/// are ever stored. Redeeming a code during sign-in is handled by the authentication
/// service (see <see cref="Authentication.IVaultAuthenticationService"/>).
/// </summary>
public interface IMfaRecoveryService
{
    /// <summary>How many unused codes the user currently holds.</summary>
    Task<int> CountRemainingAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Generates a fresh set, invalidating any codes the user held before, and returns the
    /// plaintext codes to display exactly once. Audited. A new set changes how the account signs in,
    /// so it advances the credential generation of a session issued under <paramref name="credentialGeneration"/>.
    /// </summary>
    /// <exception cref="Domain.Exceptions.StaleProofException">The account's credentials changed after the session was issued.</exception>
    Task<RecoveryCodeSet> GenerateAsync(Guid userId, long credentialGeneration, CancellationToken ct);
}

/// <summary>A newly generated set, shown once, and the credential generation it committed.</summary>
public sealed record RecoveryCodeSet(IReadOnlyList<string> Codes, long CredentialGeneration);
