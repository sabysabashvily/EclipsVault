using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.ServiceAccounts;

public sealed record ServiceAccountSummaryDto(
    Guid Id,
    string Name,
    ClearanceLevel Clearance,
    string ProjectKey,
    bool IsDisabled,
    int ActiveKeyCount,
    DateTimeOffset CreatedAtUtc);

public sealed record ApiKeyDto(
    Guid Id,
    string Prefix,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    ApiKeyStatus Status,
    ClearanceLevel? ClearanceCeiling,
    string? ProjectScope,
    bool MetadataOnly,
    IReadOnlyList<string> AllowedCidrs)
{
    /// <summary>True when the key narrows access below its service account in any dimension.</summary>
    public bool IsScoped => ClearanceCeiling is not null || !string.IsNullOrEmpty(ProjectScope) || MetadataOnly || AllowedCidrs.Count > 0;
}

public sealed record ServiceAccountDetailsDto(
    Guid Id,
    string Name,
    ClearanceLevel Clearance,
    string ProjectKey,
    bool IsDisabled,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<ApiKeyDto> Keys);

public sealed record CreateServiceAccountRequest(string Name, ClearanceLevel Clearance, string ProjectKey);

/// <summary>
/// Options for issuing a key. Every field only narrows the key below its account: an expiry, a lower
/// clearance ceiling, a single permitted project, metadata-only access, and the source ranges it may be
/// presented from.
/// </summary>
/// <param name="ExpiresInDays">0 to 3650; 0 means the key never expires.</param>
/// <param name="ClearanceCeiling">A level below the account's, or null for none.</param>
/// <param name="AllowedRanges">IP addresses and CIDR ranges as typed, separated by commas, semicolons or white space; blank for any network.</param>
public sealed record IssueApiKeyRequest(
    int ExpiresInDays,
    ClearanceLevel? ClearanceCeiling,
    string? ProjectScope,
    bool MetadataOnly,
    string? AllowedRanges);

/// <summary>Returned once, at issue time — carries the raw token, which is never persisted or shown again.</summary>
public sealed record IssuedApiKeyDto(Guid Id, string RawToken, string Prefix);

/// <summary>
/// The identity resolved from a valid API key, used to build the API caller's principal.
/// <see cref="Clearance"/> is the key's <em>effective</em> clearance (account capped by any
/// ceiling); <see cref="ProjectScope"/> and <see cref="MetadataOnly"/> carry the remaining scope.
/// </summary>
public sealed record AuthenticatedServiceAccount(
    Guid Id,
    string Name,
    ClearanceLevel Clearance,
    string ProjectKey,
    string? ProjectScope,
    bool MetadataOnly);
