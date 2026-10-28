using EclipsVault.Web.Authorization;
using EclipsVault.Web.Models;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

[ApiController]
[Route("api/ui/passwords")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PasswordsController(IBreachedPasswordScreen screen) : ControllerBase
{
    [HttpPost("check-breached")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<PasswordCheckResponse>(StatusCodes.Status200OK, "application/json")]
    public ActionResult<PasswordCheckResponse> CheckBreached(PasswordCheckRequest request)
        => new PasswordCheckResponse(!string.IsNullOrEmpty(request.Password) && screen.IsCompromised(request.Password));
}
