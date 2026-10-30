using System.Security.Claims;
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
            if (await MayDecideAsync(caller, candidate))
            {
                queue.Add(candidate.Request);
            }
        }

        return queue;
    }

    /// <summary>False for an unknown request too, so a refusal reveals nothing about the id.</summary>
    public async Task<bool> MayDecideAsync(ClaimsPrincipal caller, Guid requestId, CancellationToken ct)
        => await requests.FindForReviewAsync(requestId, ct) is { } candidate && await MayDecideAsync(caller, candidate);

    public async Task<bool> MayDecideAsync(ClaimsPrincipal caller, AccessRequestForReview candidate)
    {
        var isSomeoneElse = caller.GetUserIdOrNull() is { } callerId && callerId != candidate.Request.RequesterUserId;
        var mayShareFromProject = caller.IsAdmin()
            || string.Equals(caller.GetProject(), candidate.Secret.ProjectKey, StringComparison.OrdinalIgnoreCase);

        return isSomeoneElse
            && mayShareFromProject
            && (await authorization.AuthorizeAsync(caller, candidate.Secret, VaultPolicies.SecretAccess)).Succeeded;
    }
}
