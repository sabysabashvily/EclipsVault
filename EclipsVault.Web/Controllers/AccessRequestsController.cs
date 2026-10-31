using System.Diagnostics;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

/// <summary>
/// Filing an access request from the Legacy Denied page's form. The Access requests page itself is the
/// Angular UI's, with its UI API in <see cref="Ui.AccessRequestsController"/>; this action sends the
/// browser there with the outcome in the query string, which the page shows and then removes.
/// </summary>
public sealed class AccessRequestsController : VaultController
{
    private readonly IAccessRequestService _requests;
    private readonly IAuthorizationService _authorization;

    public AccessRequestsController(IAccessRequestService requests, IAuthorizationService authorization)
    {
        _requests = requests;
        _authorization = authorization;
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
            return Redirect("/AccessRequests?refused=secret_not_found");
        }

        return Redirect(result.Refusal is { } refusal ? $"/AccessRequests?refused={CodeOf(refusal)}" : "/AccessRequests?filed=1");
    }

    /// <summary>The refusal's code in the redirect. The Angular page holds the sentence for each one.</summary>
    private static string CodeOf(AccessRequestRefusal refusal) => refusal switch
    {
        AccessRequestRefusal.ShareCannotHelp => "share_cannot_help",
        AccessRequestRefusal.AlreadyAbleToOpen => "already_able_to_open",
        AccessRequestRefusal.AlreadyPending => "already_pending",
        AccessRequestRefusal.ReasonTooShort => "reason_too_short",
        _ => throw new UnreachableException($"No code for {refusal}."),
    };
}
