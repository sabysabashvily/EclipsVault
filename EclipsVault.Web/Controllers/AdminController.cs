using EclipsVault.Core.Application.Networks;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EclipsVault.Web.Controllers;

/// <summary>
/// Legacy trusted networks and intrusion-defence blocks.
/// </summary>
[Authorize(Policy = VaultPolicies.AdminOnly)]
public sealed class AdminController : VaultController
{
    private readonly ITrustedNetworkService _trustedNetworks;
    private readonly IIpBlacklist _blacklist;
    private readonly IBlockAdministration _blocks;
    private readonly AbacOptions _abacOptions;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        ITrustedNetworkService trustedNetworks,
        IIpBlacklist blacklist,
        IBlockAdministration blocks,
        IOptions<AbacOptions> abacOptions,
        ILogger<AdminController> logger)
    {
        _trustedNetworks = trustedNetworks;
        _blacklist = blacklist;
        _blocks = blocks;
        _abacOptions = abacOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Networks(CancellationToken ct)
        => View(await BuildNetworksViewModelAsync(ct));

    [HttpPost]
    public async Task<IActionResult> TrustCurrentIp(CancellationToken ct)
    {
        var sourceIp = HttpContext.Connection.RemoteIpAddress;
        if (sourceIp is null)
        {
            this.FlashError("The vault could not determine your source address.");
            return RedirectToAction(nameof(Networks));
        }

        var normalized = NetworkRules.Normalize(sourceIp).ToString();
        try
        {
            var added = await _trustedNetworks.AddAsync(normalized, $"Trusted from admin console by {User.Identity?.Name}", ct);
            this.FlashSuccess($"Your current address {added.Cidr} is now trusted. Access rules apply immediately.");
        }
        catch (VaultAdminException ex)
        {
            this.FlashError(ex.Message);
        }

        return RedirectToAction(nameof(Networks));
    }

    [HttpPost]
    public async Task<IActionResult> AddNetwork(AddNetworkViewModel form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            this.FlashError("Enter an IP address or CIDR range to trust.");
            return RedirectToAction(nameof(Networks));
        }

        try
        {
            var added = await _trustedNetworks.AddAsync(form.Cidr, form.Label, ct);
            this.FlashSuccess($"Trusted network {added.Cidr} added.");
        }
        catch (VaultAdminException ex)
        {
            this.FlashError(ex.Message);
        }

        return RedirectToAction(nameof(Networks));
    }

    [HttpPost]
    public async Task<IActionResult> RemoveNetwork(Guid id, CancellationToken ct)
    {
        if (await _trustedNetworks.RemoveAsync(id, ct))
        {
            this.FlashSuccess("Trusted network removed.");
        }
        else
        {
            this.FlashError("Network entry not found.");
        }

        return RedirectToAction(nameof(Networks));
    }

    [HttpPost]
    public async Task<IActionResult> Unblock(string network, CancellationToken ct)
    {
        if (await _blocks.LiftAsync(network, ct))
        {
            _logger.LogWarning("Administrator {Username} lifted the intrusion-defence block on {Network}", User.Identity?.Name, network);
            this.FlashSuccess($"Block on {network} lifted.");
        }
        else
        {
            this.FlashError("That range is not currently blocked.");
        }

        return RedirectToAction(nameof(Networks));
    }

    private async Task<NetworksViewModel> BuildNetworksViewModelAsync(CancellationToken ct)
    {
        var sourceIp = HttpContext.Connection.RemoteIpAddress;
        var currentIp = sourceIp is null ? "unknown" : NetworkRules.Normalize(sourceIp).ToString();

        var trusted = NetworkRules.IsInAnyCidr(sourceIp, _abacOptions.TrustedIpCidrs);
        if (!trusted && sourceIp is not null)
        {
            trusted = await _trustedNetworks.IsTrustedAsync(sourceIp, ct);
        }

        return new NetworksViewModel
        {
            CurrentIp = currentIp,
            CurrentIpTrusted = trusted,
            ConfiguredCidrs = _abacOptions.TrustedIpCidrs,
            DynamicNetworks = await _trustedNetworks.ListAsync(ct),
            BlockedRanges = await _blacklist.ListAsync(ct)
        };
    }

}
