using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

[ApiController]
[Route("api/ui/users")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.AdminOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(AdministratorProblemFilter))]
public sealed class UsersController(IUserAdminService users, AdministratorStepUp stepUp) : Controller
{
    [HttpGet("notice")]
    [ProducesResponseType<UserNoticeResponse>(StatusCodes.Status200OK, "application/json")]
    public ActionResult<UserNoticeResponse> Notice()
        => new UserNoticeResponse(TempData["UsersMessage"] as string, TempData["UsersError"] as bool? ?? false);

    [HttpGet]
    [ProducesResponseType<UsersResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<UsersResponse>> Get(CancellationToken ct)
    {
        var accounts = await users.ListAsync(ct);
        return new UsersResponse(accounts.Select(account => new UserResponse(
            account.Id, account.DisplayName, account.Username, account.Email, (int)account.Clearance,
            account.Clearance.ToDisplayName(), account.ProjectKey, account.TotpEnabled,
            account.IsDisabled ? "disabled" : account.IsLockedOut ? "locked" : "active",
            Url.Content($"~/Profile/Avatar?id={account.Id}&seed={Uri.EscapeDataString(account.DisplayName)}&v={(account.HasCustomAvatar ? "c" : "g")}"),
            account.Id == User.GetUserId())).ToList());
    }

    [HttpPost("{userId:guid}/enable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Enable(Guid userId, CancellationToken ct)
    {
        if (await stepUp.RequireAsync(User, AdministratorAction.EnableAccount, ct) is { } required)
        {
            return required;
        }
        return await users.SetEnabledAsync(userId, true, ct) ? NoContent() : AdministratorProblems.UserNotFound();
    }

    [HttpPost("{userId:guid}/disable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Disable(Guid userId, CancellationToken ct)
        => await users.SetEnabledAsync(userId, false, ct) ? NoContent() : AdministratorProblems.UserNotFound();

    [HttpPost("{userId:guid}/unlock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Unlock(Guid userId, CancellationToken ct)
    {
        if (await stepUp.RequireAsync(User, AdministratorAction.UnlockAccount, ct) is { } required)
        {
            return required;
        }
        return await users.UnlockAsync(userId, ct) ? NoContent() : AdministratorProblems.UserNotFound();
    }

    [HttpPost("{userId:guid}/reset-mfa")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> ResetMfa(Guid userId, CancellationToken ct)
    {
        if (await stepUp.RequireAsync(User, AdministratorAction.ResetAuthenticator, ct) is { } required)
        {
            return required;
        }
        return await users.ResetTotpAsync(userId, ct) ? NoContent() : AdministratorProblems.UserNotFound();
    }

    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid userId, CancellationToken ct)
        => await users.DeleteAsync(userId, ct) ? NoContent() : AdministratorProblems.UserNotFound();

    [HttpPost("{userId:guid}/sign-out")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> SignOut(Guid userId, CancellationToken ct)
    {
        try
        {
            return await users.ForceLogoutAsync(userId, ct) ? NoContent() : AdministratorProblems.UserNotFound();
        }
        catch (AuditWriteFailedException)
        {
            const string warning = "All of the user's sessions were revoked, but the vault couldn't record it in the audit trail.";
            if (userId == User.GetUserId())
            {
                this.FlashError(warning);
            }
            return AdministratorProblems.Problem(StatusCodes.Status503ServiceUnavailable, "revoked_unrecorded", warning);
        }
        finally
        {
            if (userId == User.GetUserId())
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        }
    }
}
