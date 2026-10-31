using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// The Access requests UI API's refusals as Problem Details with stable codes (ADR-0006). Each title is
/// the sentence Legacy flashed for the same outcome, which the page shows as it is. An audit failure is
/// left to the global 503 <c>audit_unavailable</c>.
/// </summary>
public static class AccessRequestProblems
{
    public static JsonResult RequestNotFound()
        => Problem(StatusCodes.Status404NotFound, "request_not_found", "That request was not found.");

    public static JsonResult RequestNotPending()
        => Problem(StatusCodes.Status409Conflict, "request_not_pending", "That request is no longer pending.");

    public static JsonResult ExpiryOutOfRange()
        => Problem(StatusCodes.Status400BadRequest, "validation_failed", AccessRequestService.ExpiryOutOfRange,
            new Dictionary<string, string[]> { ["expiresInDays"] = [AccessRequestService.ExpiryOutOfRange] });

    public static JsonResult Ineligible(string reason)
        => Problem(StatusCodes.Status409Conflict, "request_ineligible", reason);

    public static JsonResult ShareExists()
        => Problem(StatusCodes.Status409Conflict, "share_exists", "They already have a share on this secret.");

    private static JsonResult Problem(int status, string code, string title, IReadOnlyDictionary<string, string[]>? errors = null)
        => new(new AccessRequestProblemResponse { Status = status, Code = code, Title = title, Errors = errors })
        {
            StatusCode = status,
            ContentType = "application/problem+json"
        };
}

/// <summary>
/// Answers an approval Core refused. The titles are fixed text, never an exception's message.
/// <list type="bullet">
/// <item>A request a share can no longer help is <c>request_ineligible</c> with the reviewer's reason.</item>
/// <item>One whose secret was shredded or has expired is <c>request_ineligible</c> too: it can still be rejected.</item>
/// <item>
/// A <see cref="SharingException"/> is <c>share_exists</c>. The controller has already checked the expiry, and
/// the reviewer is never the requester, so what remains is the share another request saved first; a
/// requester deleted during the approval is answered the same way.
/// </item>
/// </list>
/// </summary>
public sealed class AccessRequestProblemFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            AccessRequestIneligibleException ineligible => AccessRequestProblems.Ineligible(AccessRequestRules.ExplainToReviewer(ineligible.Refusal)),
            SecretNotFoundException => AccessRequestProblems.Ineligible("That secret no longer exists."),
            SharingException => AccessRequestProblems.ShareExists(),
            _ => null,
        };
        context.ExceptionHandled = context.Result is not null;
    }
}
