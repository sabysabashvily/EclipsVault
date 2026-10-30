using System.ComponentModel.DataAnnotations;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Models;

public sealed class UsersViewModel
{
    public IReadOnlyList<UserSummaryDto> Users { get; init; } = [];

    public Guid CurrentUserId { get; init; }
}

public sealed class EditUserViewModel
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsDisabled { get; set; }

    public bool IsSelf { get; set; }

    [Display(Name = "Clearance level")]
    public ClearanceLevel Clearance { get; set; }

    [Required]
    [StringLength(64)]
    [Display(Name = "Project key")]
    public string ProjectKey { get; set; } = string.Empty;
}

public sealed class CreateUserViewModel
{
    [Required]
    [StringLength(UsernameRules.MaximumLength, MinimumLength = UsernameRules.MinimumLength)]
    [RegularExpression(UsernameRules.AllowedCharactersPattern, ErrorMessage = UsernameRules.Description)]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [StringLength(256, MinimumLength = 12, ErrorMessage = "Passwords must be at least 12 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Initial password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Clearance level")]
    public ClearanceLevel Clearance { get; set; } = ClearanceLevel.Standard;

    [Required]
    [StringLength(64)]
    [Display(Name = "Project key")]
    public string ProjectKey { get; set; } = string.Empty;
}

public sealed class NetworksViewModel
{
    /// <summary>The requester's address exactly as the vault sees it (VPN egress included).</summary>
    public string CurrentIp { get; init; } = string.Empty;

    public bool CurrentIpTrusted { get; init; }

    public IReadOnlyList<string> ConfiguredCidrs { get; init; } = [];

    public IReadOnlyList<TrustedNetworkDto> DynamicNetworks { get; init; } = [];

    public IReadOnlyList<BlockedRangeDto> BlockedRanges { get; init; } = [];

    public AddNetworkViewModel AddForm { get; init; } = new();
}

public sealed class AddNetworkViewModel
{
    [Required]
    [StringLength(64)]
    [Display(Name = "IP address or CIDR range")]
    public string Cidr { get; set; } = string.Empty;

    [StringLength(128)]
    [Display(Name = "Label")]
    public string Label { get; set; } = string.Empty;
}
