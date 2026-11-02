using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

/// <summary>
/// Administration of non-interactive service accounts and their API keys
/// (TopSecret clearance only). Raw key tokens are shown exactly once, at issue time.
/// </summary>
[Authorize(Policy = VaultPolicies.AdminOnly)]
public sealed class ServiceAccountsController : Controller
{
    private const string IssuedTokenKey = "IssuedApiKey";

    private readonly IServiceAccountService _accounts;

    public ServiceAccountsController(IServiceAccountService accounts) => _accounts = accounts;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await _accounts.ListAsync(ct));

    [HttpGet]
    public IActionResult Create() => View(new CreateServiceAccountViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(CreateServiceAccountViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        Guid id;
        try
        {
            id = await _accounts.CreateAsync(new CreateServiceAccountRequest(model.Name, model.Clearance, model.ProjectKey), ct);
        }
        catch (VaultAdminException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        this.FlashSuccess($"Service account '{model.Name}' created. Issue an API key to let it authenticate.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var account = await _accounts.GetAsync(id, ct);
        if (account is null)
        {
            this.FlashError("Service account not found.");
            return RedirectToAction(nameof(Index));
        }

        return View(new ServiceAccountDetailsViewModel
        {
            Account = account,
            NewlyIssuedToken = TempData[IssuedTokenKey] as string
        });
    }

    [HttpPost]
    public async Task<IActionResult> IssueKey(
        Guid id, int ttlDays, int? clearanceCeiling, string? projectScope, bool metadataOnly, string? allowedCidrs, CancellationToken ct)
    {
        // A value that fails to bind arrives as 0 days or no ceiling, the widest options, so it is refused instead.
        if (!ModelState.IsValid)
        {
            this.FlashError("Enter the expiry as a whole number of days and choose the clearance ceiling from the list.");
            return RedirectToAction(nameof(Details), new { id });
        }

        // The form's "No limit" option posts 0. Any other value goes to Core, which refuses one that is not a level below the account.
        var ceiling = clearanceCeiling is null or 0 ? (ClearanceLevel?)null : (ClearanceLevel)clearanceCeiling.Value;

        IssuedApiKeyDto? issued;
        try
        {
            var request = new IssueApiKeyRequest(ttlDays, ceiling, projectScope, metadataOnly, allowedCidrs);
            issued = await _accounts.IssueKeyAsync(id, request, ct);
        }
        catch (VaultAdminException ex)
        {
            this.FlashError(ex.Message);
            return RedirectToAction(nameof(Details), new { id });
        }

        if (issued is null)
        {
            this.FlashError("Service account not found.");
            return RedirectToAction(nameof(Index));
        }

        // Handed to the Details view once via TempData; never stored or shown again.
        TempData[IssuedTokenKey] = issued.RawToken;
        this.FlashSuccess("API key issued. Copy it now — it will not be shown again.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RevokeKey(Guid id, Guid keyId, CancellationToken ct)
    {
        if (await _accounts.RevokeKeyAsync(id, keyId, ct))
        {
            this.FlashSuccess("API key revoked.");
        }
        else
        {
            this.FlashError("Key not found.");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> SetEnabled(Guid id, bool enabled, CancellationToken ct)
    {
        if (await _accounts.SetEnabledAsync(id, enabled, ct))
        {
            this.FlashSuccess(enabled ? "Service account enabled." : "Service account disabled — all of its keys are now rejected.");
        }
        else
        {
            this.FlashError("Service account not found.");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (await _accounts.DeleteAsync(id, ct))
        {
            this.FlashSuccess("Service account and its keys deleted.");
        }
        else
        {
            this.FlashError("Service account not found.");
        }

        return RedirectToAction(nameof(Index));
    }
}
