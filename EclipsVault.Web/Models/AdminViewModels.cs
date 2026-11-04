using System.ComponentModel.DataAnnotations;

namespace EclipsVault.Web.Models;

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
