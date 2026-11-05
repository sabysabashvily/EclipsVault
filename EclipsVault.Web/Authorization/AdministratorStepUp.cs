using System.Security.Claims;
using EclipsVault.Web.Controllers.Ui;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Authorization;

/// <summary>Check the caller's freshness before looking up the account the caller wants to change.</summary>
public sealed class AdministratorStepUp(IStepUpService stepUp, IUserRepository users, TimeProvider clock)
{
    public async Task<ActionResult?> RequireAsync(ClaimsPrincipal caller, AdministratorAction action, CancellationToken ct)
    {
        if (!stepUp.IsRequired(action, caller.GetLastStrongAuthUtc(), clock.GetUtcNow()))
        {
            return null;
        }

        var account = await users.FindByIdAsync(caller.GetUserId(), ct);
        if (account is null)
        {
            return UiApiProblem.StaleSession();
        }

        return AdministratorProblems.Problem(StatusCodes.Status403Forbidden, "step_up_required",
            "Confirm it's you before granting access.", account.TotpEnabled ? "code" : "sign_in");
    }
}
