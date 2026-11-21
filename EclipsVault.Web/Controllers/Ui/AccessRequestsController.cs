using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Middleware;
using EclipsVault.Web.Models.Ui;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// The Access requests page (ADR-0027). Who may see and decide a request is <see cref="AccessRequestReviewers"/>
/// alone; withdrawing is the requester's own. A request the caller may not decide or withdraw is answered
/// exactly as an unknown one, and writes no audit row.
/// </summary>
[ApiController]
[Route("api/ui/access-requests")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(AccessRequestProblemFilter))]
public sealed class AccessRequestsController(
    IAccessRequestService requests, AccessRequestReviewers reviewers, IAntiforgery antiforgery,
    IStepUpService stepUp, IUserRepository users, IAuthorizationService authorization, TimeProvider clock) : ControllerBase
{
    /// <summary>
    /// Whether the Denied page offers the request form for a secret. It records nothing. A decoy trips and
    /// answers 404 <c>not_found</c>, as on every secret endpoint, and a secret that does not exist answers
    /// exactly the same, so the answer cannot tell which ids are decoys.
    /// </summary>
    [HttpGet("eligibility")]
    [ProducesResponseType<AccessRequestEligibilityResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<AccessRequestProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<AccessRequestEligibilityResponse>> Eligibility([FromQuery] Guid secretId, CancellationToken ct)
    {
        var callerId = User.GetUserId();
        if (callerId == Guid.Empty)
        {
            return StaleSession();
        }

        try
        {
            return await requests.FilingRefusalAsync(secretId, callerId, ct) is { } refusal
                ? new AccessRequestEligibilityResponse(false, AccessRequestProblems.CodeOf(refusal), AccessRequestRules.ExplainToRequester(refusal))
                : new AccessRequestEligibilityResponse(true, null, null);
        }
        catch (SecretNotFoundException)
        {
            return UiApiProblem.NotFound();
        }
    }

    /// <summary>
    /// Files a request. It records only its <c>AccessRequested</c> row. A decoy trips and answers 404, and a
    /// secret that does not exist answers exactly the same.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AccessRequestProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AccessRequestProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> File([FromBody] AccessRequestFilingRequest filing, CancellationToken ct)
    {
        var callerId = User.GetUserId();
        if (callerId == Guid.Empty)
        {
            return StaleSession();
        }

        AccessRequestCreateResult result;
        try
        {
            // The reviewer reads why this account was refused, so the vault's own access check says it.
            var secret = await requests.SecretToRequestAsync(filing.SecretId, ct);
            var access = await authorization.AuthorizeAsync(User, secret, VaultPolicies.SecretAccess);
            var denialReasons = access.Failure?.FailureReasons.Select(reason => reason.Message).ToList() ?? [];
            result = await requests.CreateAsync(filing.SecretId, callerId, User.GetUsername(), filing.Reason ?? string.Empty, denialReasons, ct);
        }
        catch (SecretNotFoundException)
        {
            return UiApiProblem.NotFound();
        }

        return result.Refusal is { } refusal ? AccessRequestProblems.FilingRefused(refusal) : NoContent();
    }

    [HttpGet]
    [ProducesResponseType<AccessRequestsResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<AccessRequestsResponse>> Get(CancellationToken ct)
    {
        var callerId = User.GetUserId();
        if (callerId == Guid.Empty)
        {
            return StaleSession();
        }

        var toReview = await reviewers.QueueAsync(User, ct);
        var mine = await requests.ListMineAsync(callerId, ct);
        return new AccessRequestsResponse(
            User.IsAdmin() ? "all" : "project",
            toReview.Select(request => new RequestToReviewResponse(
                request.Id,
                request.SecretName,
                request.ProjectKey,
                request.RequesterUsername,
                request.Reason,
                request.DeniedReasons,
                request.CreatedAtUtc.UtcDateTime)).ToList(),
            mine.Select(request => new FiledRequestResponse(
                request.Id,
                request.SecretName,
                request.ProjectKey,
                request.Status.ToString(),
                request.CreatedAtUtc.UtcDateTime,
                request.DecidedBy,
                request.DecidedAtUtc?.UtcDateTime,
                request.DecisionNote)).ToList());
    }

    [HttpPost("{requestId:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AccessRequestProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AccessRequestProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AccessRequestProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Approve(Guid requestId, [FromBody] AccessApprovalRequest approval, CancellationToken ct)
    {
        if (User.GetUserId() == Guid.Empty)
        {
            return StaleSession();
        }

        if (await reviewers.FindDecidableAsync(User, requestId, ct) is not { } candidate)
        {
            return AccessRequestProblems.RequestNotFound();
        }

        // Approving shares the secret, which needs the fresh proof its reveal needs.
        if (stepUp.IsRequired(candidate.Secret.Sensitivity, User.GetLastStrongAuthUtc(), clock.GetUtcNow()))
        {
            return await users.FindByIdAsync(User.GetUserId(), ct) is { } reviewer
                ? AdministratorProblems.Problem(StatusCodes.Status403Forbidden, "step_up_required",
                    "Confirm it's you before sharing this secret.", reviewer.TotpEnabled ? "code" : "sign_in")
                : StaleSession();
        }

        if (approval.ExpiresInDays is not { } days || days is < 0 or > AccessRequestService.LongestExpiryDays)
        {
            return AccessRequestProblems.ExpiryOutOfRange();
        }

        return await requests.ApproveAsync(requestId, days, ct)
            ? NoContent()
            : AccessRequestProblems.RequestNotPending();
    }

    [HttpPost("{requestId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AccessRequestProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AccessRequestProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Reject(Guid requestId, CancellationToken ct)
    {
        if (User.GetUserId() == Guid.Empty)
        {
            return StaleSession();
        }

        if (!await reviewers.MayDecideAsync(User, requestId, ct))
        {
            return AccessRequestProblems.RequestNotFound();
        }

        return await requests.RejectAsync(requestId, ct) ? NoContent() : AccessRequestProblems.RequestNotPending();
    }

    [HttpPost("{requestId:guid}/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AccessRequestProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AccessRequestProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Withdraw(Guid requestId, CancellationToken ct)
    {
        var requesterId = User.GetUserId();
        if (requesterId == Guid.Empty)
        {
            return StaleSession();
        }

        // Only the caller's own requests are looked at, so someone else's answers as an unknown id does.
        if ((await requests.ListMineAsync(requesterId, ct)).All(request => request.Id != requestId))
        {
            return AccessRequestProblems.RequestNotFound();
        }

        return await requests.CancelAsync(requestId, requesterId, ct) ? NoContent() : AccessRequestProblems.RequestNotPending();
    }

    private ActionResult StaleSession()
    {
        UiAntiforgery.IssueToken(HttpContext, antiforgery);
        return UiApiProblem.StaleSession();
    }
}
