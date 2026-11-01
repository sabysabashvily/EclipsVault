using System.Security.Claims;
using EclipsVault.Core.Application.Abac;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authorization;

namespace EclipsVault.Web.Authorization;

/// <summary>
/// Who may see and decide an access request (ADR-0027): anyone who could issue the share from the secret
/// itself right now, that is an administrator or a member of the secret's project who passes the secret's
/// access check, other than the requester. The check is <see cref="SecretAccessHandler"/>, the one the
/// Secrets list and the sharing panel run, so a reviewer never decides on a secret they could not open.
///
/// Every page and endpoint that lists or decides requests asks here, so the rule is written once.
/// </summary>
public sealed class AccessRequestReviewers(IAccessRequestService requests, IAuthorizationService authorization)
{
    /// <summary>The pending requests this caller could decide now, newest first.</summary>
    public async Task<IReadOnlyList<AccessRequestDto>> QueueAsync(ClaimsPrincipal caller, CancellationToken ct)
    {
        var queue = new List<AccessRequestDto>();
        foreach (var candidate in await requests.ListPendingForReviewAsync(ct))
        {
            // Listing the queue enumerates secrets, as the Secrets list does, so the access check does not log
            // a denied read for each request it hides. The two kinds differ only for metadata-only API keys,
            // which never reach this page.
            if (await MayDecideAsync(caller, candidate, AccessKind.Enumerate))
            {
                queue.Add(candidate.Request);
            }
        }

        return queue;
    }

    /// <summary>False for an unknown request too, so a refusal reveals nothing about the id.</summary>
    public async Task<bool> MayDecideAsync(ClaimsPrincipal caller, Guid requestId, CancellationToken ct)
        => await requests.FindForReviewAsync(requestId, ct) is { } candidate && await MayDecideAsync(caller, candidate, AccessKind.Read);

    private async Task<bool> MayDecideAsync(ClaimsPrincipal caller, AccessRequestForReview candidate, AccessKind access)
    {
        var isSomeoneElse = caller.GetUserIdOrNull() is { } callerId && callerId != candidate.Request.RequesterUserId;
        var mayShareFromProject = caller.IsAdmin()
            || string.Equals(caller.GetProject(), candidate.Secret.ProjectKey, StringComparison.OrdinalIgnoreCase);

        return isSomeoneElse
            && mayShareFromProject
            && (await authorization.AuthorizeAsync(caller, candidate.Secret, new SecretAccessRequirement(access))).Succeeded;
    }
}
