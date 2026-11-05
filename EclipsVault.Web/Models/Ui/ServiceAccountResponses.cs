using System.ComponentModel.DataAnnotations;

namespace EclipsVault.Web.Models.Ui;

public sealed record ServiceAccountsResponse(IReadOnlyList<ServiceAccountResponse> ServiceAccounts);

public sealed record ServiceAccountResponse(Guid ServiceAccountId, string Name, int Clearance, string ClearanceLabel,
    string Project, int ActiveKeys, bool Enabled, DateTimeOffset CreatedAtUtc);

public sealed record NewServiceAccountResponse(IReadOnlyList<ClearanceOptionResponse> Clearances);

public sealed record CreatedServiceAccountResponse(Guid ServiceAccountId);

public sealed record ServiceAccountDetailsResponse(Guid ServiceAccountId, string Name, int Clearance, string ClearanceLabel,
    string Project, bool Enabled, DateTimeOffset CreatedAtUtc, IReadOnlyList<ClearanceOptionResponse> Ceilings,
    IReadOnlyList<ServiceAccountKeyResponse> Keys);

public sealed record ServiceAccountKeyResponse(Guid KeyId, string Prefix, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc, DateTimeOffset? LastUsedAtUtc,
    [property: AllowedValues("active", "expired", "revoked")] string Status, bool FullAccess, bool MetadataOnly,
    string? ClearanceCeilingLabel, string? ProjectScope, IReadOnlyList<string> AllowedRanges);

public sealed record IssuedServiceAccountKeyResponse(Guid KeyId, string Prefix, string ApiKey);

public sealed class CreateServiceAccountUiRequest
{
    public string? Name { get; init; }
    public int? Clearance { get; init; }
    public string? Project { get; init; }
}

public sealed class IssueServiceAccountKeyRequest
{
    [Required]
    public int? ExpiresInDays { get; init; }
    public int? ClearanceCeiling { get; init; }
    public string? ProjectScope { get; init; }
    public bool MetadataOnly { get; init; }
    public string? AllowedRanges { get; init; }
}
