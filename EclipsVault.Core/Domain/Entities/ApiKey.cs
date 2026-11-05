using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Domain.Entities;

/// <summary>
/// A bearer credential for a <see cref="ServiceAccount"/>. Only the SHA-256 hash of
/// the token is stored — the raw token is shown once at issue time and never again.
/// Keys can carry an expiry and be revoked.
///
/// A key may also carry a <em>scope</em> that narrows it <b>below</b> its service
/// account's own attributes (never above): a lower clearance ceiling, a single
/// permitted project, and/or metadata-only access. Scope is enforced by the same ABAC
/// engine that governs interactive users, so a scoped key can only ever see less.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; }

    public Guid ServiceAccountId { get; set; }

    public ServiceAccount? ServiceAccount { get; set; }

    /// <summary>SHA-256 (hex) of the full token; the lookup + verification value.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Non-secret leading fragment of the token, for display (e.g. "evk_Ab12cd…").</summary>
    public string Prefix { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    /// <summary>When set, caps the key's effective clearance below the account's. Never raises it.</summary>
    public ClearanceLevel? ClearanceCeiling { get; set; }

    /// <summary>When set, restricts the key to a single project — enforced even for a TopSecret account.</summary>
    public string? ProjectScope { get; set; }

    /// <summary>When true, the key may list secret metadata but never read (decrypt) a value.</summary>
    public bool MetadataOnly { get; set; }

    /// <summary>
    /// Optional network binding: a ';'-separated list of CIDR ranges the key may be presented
    /// from. Null or empty means no restriction. A leaked key is useless off these ranges.
    /// </summary>
    public string? AllowedCidrs { get; set; }

    public bool IsActive(DateTimeOffset nowUtc)
        => RevokedAtUtc is null && (ExpiresAtUtc is null || ExpiresAtUtc > nowUtc);

    public ApiKeyStatus StatusAt(DateTimeOffset nowUtc)
        => RevokedAtUtc is not null ? ApiKeyStatus.Revoked
            : IsActive(nowUtc) ? ApiKeyStatus.Active
            : ApiKeyStatus.Expired;

    /// <summary>The network binding split into individual CIDR ranges (empty when unrestricted).</summary>
    public IReadOnlyList<string> AllowedCidrList()
        => string.IsNullOrEmpty(AllowedCidrs)
            ? []
            : AllowedCidrs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The clearance this key actually acts with: the account's, capped by any ceiling.</summary>
    public ClearanceLevel EffectiveClearance(ClearanceLevel accountClearance)
        => ClearanceCeiling is { } ceiling && (int)ceiling < (int)accountClearance ? ceiling : accountClearance;
}
