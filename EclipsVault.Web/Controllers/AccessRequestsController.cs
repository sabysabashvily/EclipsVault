using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

/// <summary>
/// Self-service access requests. Any signed-in user can file a request for a secret a share could open
/// for them; a reviewer (<see cref="AccessRequestReviewers"/>) approves it, which shares the secret, or
/// rejects it. Everything here is audited.
/// </summary>
public sealed class AccessRequestsController : VaultController
{
    private const string RequestNotFound = "That request was not found.";

    private readonly IAccessRequestService _requests;
    private readonly AccessRequestReviewers _reviewers;
    private readonly IAuthorizationService _authorization;

    public AccessRequestsController(IAccessRequestService requests, AccessRequestReviewers reviewers, IAuthorizationService authorization)
    {
        _requests = requests;
        _reviewers = reviewers;
        _authorization = authorization;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var toReview = await _reviewers.QueueAsync(User, ct);
        var mine = await _requests.ListMineAsync(CurrentUserId(), ct);
        return View(new AccessRequestsViewModel { ToReview = toReview, Mine = mine, CanReviewAll = User.IsAdmin() });
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid secretId, string reason, CancellationToken ct)
    {
        AccessRequestCreateResult result;
        try
        {
            // The reviewer reads why this account was refused, so the vault's own access check says it.
            var secret = await _requests.SecretToRequestAsync(secretId, ct);
            var access = await _authorization.AuthorizeAsync(User, secret, VaultPolicies.SecretAccess);
            var denialReasons = access.Failure?.FailureReasons.Select(r => r.Message).ToList() ?? [];
            result = await _requests.CreateAsync(secretId, CurrentUserId(), CurrentUsername(), reason ?? string.Empty, denialReasons, ct);
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
        if (!await _reviewers.MayDecideAsync(User, id, ct))
        {
            return RefuseAsNotFound();
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
        catch (Exception ex) when (ex is SharingException or AccessRequestIneligibleException)
        {
            this.FlashError(ex.Message);
        }
        catch (SecretNotFoundException)
        {
            this.FlashError("That secret no longer exists.");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        if (!await _reviewers.MayDecideAsync(User, id, ct))
        {
            return RefuseAsNotFound();
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

    /// <summary>Answers a request the caller may not decide exactly as an unknown one.</summary>
    private RedirectToActionResult RefuseAsNotFound()
    {
        this.FlashError(RequestNotFound);
        return RedirectToAction(nameof(Index));
    }
}
