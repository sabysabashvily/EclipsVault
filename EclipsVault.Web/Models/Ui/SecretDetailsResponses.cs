using System.ComponentModel.DataAnnotations;
using EclipsVault.Core.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// One secret's page. <see cref="Shares"/> is null unless the caller may share it, and
/// <see cref="Managed"/> is null for a secret no backend login is bound to.
/// </summary>
public sealed record SecretDetailsResponse(
    Guid SecretId,
    string Name,
    string Project,
    [property: UiEnum(typeof(SecretEnvironment))] string Environment,
    int Sensitivity,
    string SensitivityLabel,
    string Algorithm,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    ManagedSecretResponse? Managed,
    IReadOnlyList<SecretVersionResponse> Versions,
    bool CanChange,
    bool CanShare,
    IReadOnlyList<SecretShareResponse>? Shares,
    int StepUpMaxAgeMinutes);

/// <summary>
/// The backend login a managed secret is the password of, and the licence note under it.
/// <see cref="EvaluationKnown"/> is false when the evaluation could not be read, so the page leaves the
/// note out; otherwise <see cref="EvaluationDaysRemaining"/> is null before the first rotation.
/// </summary>
public sealed record ManagedSecretResponse(string Principal, bool Licensed, bool EvaluationKnown, int? EvaluationDaysRemaining);

public sealed record SecretVersionResponse(Guid VersionId, int VersionNumber, DateTimeOffset ArchivedAtUtc, string ArchivedBy, string? ChangeNote);

public sealed record SecretShareResponse(Guid ShareId, string Recipient, string Issuer, DateTimeOffset SinceUtc, DateTimeOffset? ExpiresAtUtc);

/// <summary>A decrypted value, labelled "current value" or "version n". It is never cached.</summary>
public sealed record RevealedValueResponse(string Label, string Value);

/// <summary>Rotate, with the Legacy form's rules and display names. The action validates it, not MVC.</summary>
[ValidateNever]
public sealed class RotateSecretUiRequest
{
    [Required]
    [StringLength(8192)]
    [Display(Name = "New value")]
    public string? NewValue { get; init; }

    [StringLength(256)]
    [Display(Name = "Change note (optional)")]
    public string? ChangeNote { get; init; }

    /// <summary>Null leaves the existing expiry alone; a value resets it to that many days from now.</summary>
    [Range(1, 3650)]
    [Display(Name = "Renew for (days)")]
    public int? RenewTtlDays { get; init; }
}

[ValidateNever]
public sealed class RotateManagedUiRequest
{
    [Range(1, 3650)]
    [Display(Name = "Renew for (days)")]
    public int? RenewTtlDays { get; init; }
}

[ValidateNever]
public sealed class ShareSecretUiRequest
{
    [Required]
    [StringLength(256)]
    [Display(Name = "User (username or email)")]
    public string? Recipient { get; init; }

    [Range(0, 3650)]
    [Display(Name = "Access expires in days (0 = no expiry)")]
    public int TtlDays { get; init; }
}
