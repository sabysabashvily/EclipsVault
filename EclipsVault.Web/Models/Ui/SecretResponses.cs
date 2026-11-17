using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using EclipsVault.Core.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

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

/// <summary>
/// New secret, with the Legacy form's rules and display names, so a refused field reads word for word as
/// Legacy's form did. The action validates it, not MVC, whose answer would name the field only.
/// </summary>
[ValidateNever]
public sealed class CreateSecretUiRequest
{
    [Required]
    [StringLength(128, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9_\-\.]+$", ErrorMessage = "Use letters, digits, underscores, hyphens or dots.")]
    [Display(Name = "Name")]
    public string? Name { get; init; }

    [Required]
    [StringLength(8192)]
    [Display(Name = "Secret value")]
    public string? Value { get; init; }

    [Required]
    [StringLength(64)]
    [Display(Name = "Project key")]
    public string? Project { get; init; }

    [UiEnum(typeof(SecretEnvironment))]
    public string? Environment { get; init; }

    public int? Sensitivity { get; init; }

    /// <summary>Null when the field was left empty, which Legacy refused.</summary>
    [Range(0, 3650)]
    [Display(Name = "TTL in days (0 = never expires)")]
    public int? TtlDays { get; init; }
}

public sealed record CreatedSecretResponse(Guid SecretId);

/// <summary>
/// A refused secret write. <see cref="Errors"/> names each refused field with its Legacy message for
/// <c>validation_failed</c>; <see cref="Reasons"/> carries the policy's reasons for <c>creation_denied</c>.
/// </summary>
public sealed class SecretProblemResponse : ProblemDetails
{
    public required string Code { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Reasons { get; init; }
}
