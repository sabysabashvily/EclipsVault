using System.ComponentModel.DataAnnotations;

namespace EclipsVault.Web.Models;

public sealed class DynamicSecretsViewModel
{
    /// <summary>Only the roles the caller's clearance and project actually permit.</summary>
    public IReadOnlyList<DynamicSecretRoleDto> Roles { get; init; } = [];

    public IReadOnlyList<LeaseDto> Leases { get; init; } = [];

    /// <summary>Admins see everyone's leases; everyone else sees their own.</summary>
    public bool ShowingEveryone { get; init; }

    /// <summary>
    /// Set for exactly one render, immediately after issuing. The vault stores no copy, so this is
    /// the only time the credential can be shown — reloading the page loses it for good.
    /// </summary>
    public IssuedCredentialDto? Issued { get; init; }

    /// <summary>False when the vault holds no licence for dynamic secrets — the page nudges rather than hides.</summary>
    public bool Licensed { get; init; } = true;

    /// <summary>True when the evaluation window could not be read, so the licence note is left out.</summary>
    public bool LicenceNoteUnavailable { get; init; }

    /// <summary>
    /// Days left in the licence's evaluation period, or null when it has not started. Only
    /// meaningful while <see cref="Licensed"/> is false: null means the period is still available,
    /// a positive number means it is running, and zero means it is spent.
    /// </summary>
    public int? EvaluationDaysRemaining { get; init; }
}

public sealed class IssueCredentialViewModel
{
    [Required]
    public Guid RoleId { get; set; }

    /// <summary>Null takes the role's default; the service clamps anything above its ceiling.</summary>
    [Range(1, 1440)]
    [Display(Name = "Lease (minutes)")]
    public int? TtlMinutes { get; set; }
}
