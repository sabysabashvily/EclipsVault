namespace EclipsVault.Core.Domain.Entities;

/// <summary>
/// A single-use, expiring activation invitation for an account awaiting activation (ADR-0033).
/// Only the SHA-256 hash of the 256-bit token is persisted; resending replaces it and clears any
/// pending authenticator seed.
/// </summary>
public class AccountInvitation
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>SHA-256 digest (32 bytes) of the raw invitation token.</summary>
    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>When the token was spent by activation or by replacing the invitation address.</summary>
    public DateTimeOffset? SpentAtUtc { get; set; }

    /// <summary>Base32-encoded TOTP seed generated once on inspection, kept until activation commits or the token is replaced.</summary>
    public string? PendingTotpSecret { get; set; }

    public bool IsUsable(DateTimeOffset nowUtc) => SpentAtUtc is null && ExpiresAtUtc > nowUtc;
}
