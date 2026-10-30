using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

/// <summary>
/// Self-service access requests. Any signed-in user can file a request for a secret they were
/// denied; a reviewer (an administrator, or a member of the secret's project) approves it —
/// which creates an ordinary grant — or rejects it. Everything here is audited.
/// </summary>
public sealed class AccessRequestsController : VaultController
{
    private readonly IAccessRequestService _requests;
    private readonly ISecretService _secrets;

    public AccessRequestsController(IAccessRequestService requests, ISecretService secrets)
    {
        _requests = requests;
        _secrets = secrets;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var toReview = await _requests.ListToReviewAsync(IsAdmin(), CurrentProject(), ct);
        var mine = await _requests.ListMineAsync(CurrentUserId(), ct);
        return View(new AccessRequestsViewModel { ToReview = toReview, Mine = mine, CanReviewAll = IsAdmin() });
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid secretId, string reason, string? deniedReasons, CancellationToken ct)
    {
        // Look up the authoritative name/project (never trust posted values); a honey-token id
        // trips the trap here just as it would anywhere else.
        SecretDetailsDto secret;
        try
        {
            secret = await _secrets.GetDetailsAsync(secretId, ct);
        }
        catch (HoneyTokenTrippedException)
        {
            return NotFound();
        }
        catch (SecretNotFoundException)
        {
            this.FlashError("That secret no longer exists.");
            return RedirectToAction(nameof(Index));
        }

        var result = await _requests.CreateAsync(
            secretId, secret.Name, secret.ProjectKey, CurrentUserId(), CurrentUsername(), reason ?? string.Empty, deniedReasons, ct);

        if (result.Created)
        {
            this.FlashSuccess("Access request submitted. A reviewer for this secret will decide.");
        }
        else
        {
            this.FlashError(result.Error ?? "The request could not be submitted.");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Approve(Guid id, int ttlDays, CancellationToken ct)
    {
        if (await AuthorizeReviewAsync(id, ct) is { } forbidden)
        {
            return forbidden;
        }

        // An expiry that is not a whole number binds as 0, which would approve with no expiry.
        // Core checks the range itself.
        if (!ModelState.IsValid)
        {
            this.FlashError(AccessRequestService.ExpiryOutOfRange);
            return RedirectToAction(nameof(Index));
        }

        try
        {
            if (await _requests.ApproveAsync(id, ttlDays, ct))
            {
                this.FlashSuccess("Request approved — a grant was created for the requester.");
            }
            else
            {
                this.FlashError("That request is no longer pending.");
            }
        }
        catch (SharingException ex)
        {
            this.FlashError(ex.Message);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        if (await AuthorizeReviewAsync(id, ct) is { } forbidden)
        {
            return forbidden;
        }

        if (await _requests.RejectAsync(id, ct))
        {
            this.FlashInfo("Request rejected.");
        }
        else
        {
            this.FlashError("That request is no longer pending.");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (await _requests.CancelAsync(id, CurrentUserId(), ct))
        {
            this.FlashInfo("Request withdrawn.");
        }
        else
        {
            this.FlashError("That request is no longer pending.");
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>Null when the caller may review the request; a Forbid/redirect otherwise.</summary>
    private async Task<IActionResult?> AuthorizeReviewAsync(Guid id, CancellationToken ct)
    {
        var request = await _requests.GetAsync(id, ct);
        if (request is null)
        {
            this.FlashError("That request was not found.");
            return RedirectToAction(nameof(Index));
        }

        var canReview = IsAdmin() || string.Equals(CurrentProject(), request.ProjectKey, StringComparison.OrdinalIgnoreCase);
        return canReview ? null : Forbid();
    }

    private bool IsAdmin() => User.IsAdmin();

    private string CurrentProject() => User.GetProject();
}
