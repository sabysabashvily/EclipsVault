using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using EclipsVault.Core.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Models.Ui;

/// <summary>The Secrets list: exactly the secrets the caller could open now, never a decoy.</summary>
public sealed record SecretsResponse(IReadOnlyList<SecretListItemResponse> Secrets);

public sealed record SecretListItemResponse(
    Guid SecretId,
    string Name,
    string Project,
    [property: UiEnum(typeof(SecretEnvironment))] string Environment,
    int Sensitivity,
    string SensitivityLabel,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>The palette search's answer: a bare array, as the Legacy search answered and both palettes read it.</summary>
public sealed class SecretSearchResponse(IEnumerable<SecretSearchResultResponse> results) : List<SecretSearchResultResponse>(results);

/// <summary>
/// One palette search result, in the Legacy search's shape: the number picks the badge's colour, the
/// name is what it reads, and <paramref name="Url"/> opens the secret's details.
/// </summary>
public sealed record SecretSearchResultResponse(
    Guid Id,
    string Name,
    string Project,
    [property: UiEnum(typeof(SecretEnvironment))] string Environment,
    int Sensitivity,
    string SensitivityName,
    string Url);

/// <summary>New secret, with the Legacy form's limits.</summary>
public sealed class CreateSecretUiRequest
{
    [Required]
    [StringLength(128, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9_\-\.]+$")]
    public string? Name { get; init; }

    [Required]
    [StringLength(8192)]
    public string? Value { get; init; }

    [Required]
    [StringLength(64)]
    public string? Project { get; init; }

    [Required]
    [UiEnum(typeof(SecretEnvironment))]
    public string? Environment { get; init; }

    [Required]
    public int? Sensitivity { get; init; }

    [Range(0, 3650)]
    public int TtlDays { get; init; }
}

public sealed record CreatedSecretResponse(Guid SecretId);

/// <summary>A refused secret write. <see cref="Reasons"/> carries the policy's reasons for <c>creation_denied</c>.</summary>
public sealed class SecretProblemResponse : ProblemDetails
{
    public required string Code { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Reasons { get; init; }
}
