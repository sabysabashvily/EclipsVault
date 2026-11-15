using System.ComponentModel.DataAnnotations;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Models;

public sealed class SecretDetailsViewModel
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ProjectKey { get; init; } = string.Empty;

    public SecretEnvironment Environment { get; init; }

    public SensitivityLevel Sensitivity { get; init; }

    public string Algorithm { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>True when the vault can change the real credential, not just the copy it stores.</summary>
    public bool IsManaged { get; init; }

    /// <summary>The backend principal this secret is the password for, when managed.</summary>
    public string? RotationPrincipal { get; init; }

    /// <summary>Populated only for the single response following an authorized reveal.</summary>
    public string? RevealedValue { get; init; }

    /// <summary>Label of what was revealed ("current value" or "version N"); null when nothing is revealed.</summary>
    public string? RevealedLabel { get; init; }

    /// <summary>Archived (superseded) values, newest first.</summary>
    public IReadOnlyList<SecretVersionDto> Versions { get; init; } = [];

    /// <summary>True when the current user may manage sharing (admin, or a member of the secret's project).</summary>
    public bool CanShare { get; init; }

    /// <summary>Active access grants on this secret (populated only when <see cref="CanShare"/>).</summary>
    public IReadOnlyList<SecretGrantDto> Grants { get; init; } = [];

    /// <summary>True when a reveal was attempted but a fresh re-authentication is required first.</summary>
    public bool StepUpRequired { get; init; }

    /// <summary>Set when a submitted step-up code was wrong.</summary>
    public string? StepUpError { get; init; }

    /// <summary>Carries the archived version being revealed through the step-up round-trip (null for the current value).</summary>
    public Guid? StepUpVersionId { get; init; }

    /// <summary>The configured freshness window, for the step-up prompt's explanation.</summary>
    public int StepUpMaxAgeMinutes { get; init; }

    /// <summary>False when the vault holds no licence for managed rotation — the page nudges rather than hides.</summary>
    public bool ManagedRotationLicensed { get; init; } = true;

    /// <summary>
    /// Days left in managed rotation's evaluation period, or null when it has not started. Only
    /// meaningful while <see cref="ManagedRotationLicensed"/> is false.
    /// </summary>
    public int? ManagedRotationEvaluationDaysRemaining { get; init; }
}

public sealed class ShareSecretViewModel
{
    public Guid SecretId { get; set; }

    [Required]
    [StringLength(256)]
    [Display(Name = "User (username or email)")]
    public string GranteeUsernameOrEmail { get; set; } = string.Empty;

    [Range(0, 3650)]
    [Display(Name = "Access expires in days (0 = no expiry)")]
    public int TtlDays { get; set; }
}

public sealed class RotateSecretViewModel
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(8192)]
    [DataType(DataType.MultilineText)]
    [Display(Name = "New value")]
    public string NewValue { get; set; } = string.Empty;

    [StringLength(256)]
    [Display(Name = "Change note (optional)")]
    public string? ChangeNote { get; set; }

    /// <summary>Null leaves the existing expiry alone; a value resets it to that many days from now.</summary>
    [Range(1, 3650)]
    [Display(Name = "Renew for (days)")]
    public int? RenewTtlDays { get; set; }
}
