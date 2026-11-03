using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EclipsVault.Web.Controllers;

/// <summary>
/// Legacy staff provisioning and role forms, trusted networks and intrusion-defence blocks.
/// </summary>
[Authorize(Policy = VaultPolicies.AdminOnly)]
public sealed class AdminController : VaultController
{
    private readonly IUserAdminService _userAdmin;
    private readonly ITrustedNetworkService _trustedNetworks;
    private readonly IIpBlacklist _blacklist;
    private readonly IAuditSink _audit;
    private readonly AbacOptions _abacOptions;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IUserAdminService userAdmin,
        ITrustedNetworkService trustedNetworks,
        IIpBlacklist blacklist,
        IAuditSink audit,
        IOptions<AbacOptions> abacOptions,
        ILogger<AdminController> logger)
    {
        _userAdmin = userAdmin;
        _trustedNetworks = trustedNetworks;
        _blacklist = blacklist;
        _audit = audit;
        _abacOptions = abacOptions.Value;
        _logger = logger;
    }

    // ---- Users -------------------------------------------------------------------

    [HttpGet]
    public IActionResult CreateUser() => View(new CreateUserViewModel());

    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        CreatedUserDto created;
        try
        {
            created = await _userAdmin.CreateAsync(
                new CreateUserRequest(model.Username, model.FirstName, model.LastName, model.Password, model.Clearance, model.ProjectKey),
                ct);
        }
        catch (VaultAdminException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        UsersSuccess($"User '{created.Username}' created with email {created.Email}. They can sign in with either and will set up their authenticator on first sign-in.");
        return Redirect("/Admin/Users");
    }

    [HttpGet]
    public async Task<IActionResult> EditUser(Guid id, CancellationToken ct)
    {
        var user = await _userAdmin.GetAsync(id, ct);
        if (user is null)
        {
            UsersError("User not found.");
            return Redirect("/Admin/Users");
        }

        return View(new EditUserViewModel
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Clearance = user.Clearance,
            ProjectKey = user.ProjectKey,
            IsDisabled = user.IsDisabled,
            IsSelf = user.Id == CurrentUserId()
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditUser(EditUserViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var account = await _userAdmin.GetAsync(model.Id, ct);
            if (account is null)
            {
                UsersError("User not found.");
                return Redirect("/Admin/Users");
            }

            if (await _userAdmin.SetRoleAsync(model.Id, model.Clearance, model.ProjectKey, ct))
            {
                UsersSuccess($"Role updated for '{account.Username}'. Their sessions were revoked so the change takes effect at next sign-in.");
            }
            else
            {
                UsersError("User not found.");
            }
        }
        catch (VaultAdminException ex)
        {
            model.IsSelf = model.Id == CurrentUserId();
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        return Redirect("/Admin/Users");
    }

    private void UsersSuccess(string message)
    {
        TempData["UsersMessage"] = message;
        TempData["UsersError"] = false;
    }

    private void UsersError(string message)
    {
        TempData["UsersMessage"] = message;
        TempData["UsersError"] = true;
    }

    // ---- Networks ----------------------------------------------------------------

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
        if (await _blacklist.UnblockAsync(network, ct))
        {
            await _audit.WriteAsync(new AuditEntry
            {
                Action = AuditAction.IpRangeUnblocked,
                ResourceType = "TrustedNetwork",
                ResourceName = network,
                Details = "Intrusion-defence block lifted"
            }, ct);
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
