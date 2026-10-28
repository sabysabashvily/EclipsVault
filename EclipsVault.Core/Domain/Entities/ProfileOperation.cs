using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Domain.Entities;

/// <summary>
/// One security change to a vault account, prepared and frozen on the server before its owner proves
/// an existing credential for it (ADR-0022). The proof authorizes this change alone, for a short time,
/// and only for the session that prepared it. Committing the change deletes the operation.
/// </summary>
public class ProfileOperation
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The session that prepared the change. No other session can verify or commit it.</summary>
    public Guid SessionId { get; set; }

    /// <summary>The credential generation the preparing session was issued under.</summary>
    public long CredentialGeneration { get; set; }

    /// <summary>The session revocation version the preparing session was issued under.</summary>
    public string RevocationVersion { get; set; } = string.Empty;

    public ProfileOperationKind Kind { get; set; }

    /// <summary>The new password's Argon2id hash, for a password change. The password itself is never stored.</summary>
    public byte[]? CandidatePasswordHash { get; set; }

    public byte[]? CandidatePasswordSalt { get; set; }

    /// <summary>The name a passkey being added will get.</summary>
    public string? PasskeyNickname { get; set; }

    /// <summary>The passkey being removed.</summary>
    public Guid? TargetPasskeyId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>An operation not verified by this instant can no longer be verified.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? VerifiedAtUtc { get; set; }

    public AccountProofMethod? ProofMethod { get; set; }

    /// <summary>The one passkey challenge in flight for this operation; issuing another replaces it.</summary>
    public string? Challenge { get; set; }

    public PasskeyCeremonyPurpose? ChallengePurpose { get; set; }

    public DateTimeOffset? ChallengeExpiresAtUtc { get; set; }
}
