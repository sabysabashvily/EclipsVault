using EclipsVault.Core.Application.Sso;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace EclipsVault.Web.Controllers;

/// <summary>
/// What sign-in still serves from the Legacy UI: single sign-on's two server redirects, break-glass
/// recovery, the Error page's sign-out and the Denied page. The Angular UI serves the sign-in pages
/// themselves, on the UI API's calls (ADR-0032).
/// </summary>
// Default-deny at the class, opened per action, so every anonymous action here is declared where it is.
[Authorize]
[ServiceFilter(typeof(AuthThrottleFilter))]
public sealed class AccountController : Controller
{
    private readonly IVaultAuthenticationService _auth;
    private readonly IIpBlacklist _blacklist;
    private readonly AccountSignOut _signOut;
    private readonly IAuditSink _audit;
    private readonly ISsoSignInService _sso;
    private readonly SessionIssuer _issuer;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IVaultAuthenticationService auth,
        IIpBlacklist blacklist,
        AccountSignOut signOut,
        IAuditSink audit,
        ISsoSignInService sso,
        SessionIssuer issuer,
        ILogger<AccountController> logger)
    {
        _auth = auth;
        _blacklist = blacklist;
        _signOut = signOut;
        _audit = audit;
        _sso = sso;
        _issuer = issuer;
        _logger = logger;
    }

    /// <summary>
    /// Break-glass recovery for administrators locked out by the intrusion defence.
    /// The IP-blacklist middleware exempts only this endpoint; it demands all factors
    /// (password + TOTP) in one step, is restricted to TopSecret clearance, heavily
    /// rate limited, and every attempt is audited.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Recover() => View(new RecoverViewModel());

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Recover(RecoverViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var sourceIp = HttpContext.Connection.RemoteIpAddress;

        var credentials = await _auth.ValidateCredentialsAsync(model.Username, model.Password, ct);
        var stamp = credentials.Stamp;
        var user = credentials.Status == CredentialStatus.RequiresTotp && credentials.User is not null && stamp is not null
            ? await _auth.VerifyBreakGlassAsync(credentials.User.Id, stamp, model.Code, ct)
            : null;

        if (user is null || stamp is null)
        {
            _logger.LogWarning("Break-glass recovery failed for {Username} from {SourceIp}", model.Username, sourceIp);
            ModelState.AddModelError(string.Empty, "Recovery could not be completed with these credentials.");
            return View(model);
        }

        var finished = await new BreakGlassSignIn(_issuer, _blacklist, _audit)
            .FinishAsync(HttpContext, user, stamp, sourceIp, ct);
        if (!finished.SessionIssued)
        {
            ModelState.AddModelError(string.Empty, "Recovery could not be completed with these credentials.");
            return View(model);
        }

        _logger.LogWarning(
            "Break-glass recovery completed by {Username} from {SourceIp}; block lifted: {BlockLifted}",
            user.Username, sourceIp, finished.BlockLifted);

        this.FlashSuccess(finished.BlockLifted
            ? "The intrusion-defence block on your network was lifted and your session restored."
            : "Access restored. Your network was not blocked.");
        return LocalRedirect(SignInTarget.AfterBreakGlass);
    }

    /// <summary>The Error page's sign-out until Area 9; the UI API's sign-out runs the same implementation.</summary>
    [HttpPost]
    public async Task<IActionResult> Logout(CancellationToken ct)
        => await _signOut.ThisSessionAsync(HttpContext, ct)
            ? RedirectToAction("Index", "Home")
            : LocalRedirect("/Account/Login?signOut=sign_out_incomplete");

    /// <summary>TempData key used to hand ABAC denial reasons to the Denied page ('\n'-separated).</summary>
    internal const string DenialReasonsTempDataKey = "AbacDenialReasons";

    [HttpGet]
    public async Task<IActionResult> Denied(Guid? secretId, [FromServices] IAccessRequestService requests, CancellationToken ct)
        => View(new AccessDeniedViewModel
        {
            Reasons = TempData[DenialReasonsTempDataKey] is string reasons
                ? reasons.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                : [],
            SecretId = secretId,
            RequestRefusalMessage = secretId is { } id ? await RequestRefusalMessageAsync(requests, id, ct) : null,
        });

    /// <summary>
    /// Why the Request access form is not offered for this secret, or null when it is. A honey token's
    /// exception is left to the global handler, which signs the session out.
    /// </summary>
    private async Task<string?> RequestRefusalMessageAsync(IAccessRequestService requests, Guid secretId, CancellationToken ct)
    {
        try
        {
            return await requests.FilingRefusalAsync(secretId, User.GetUserId(), ct) is { } refusal
                ? AccessRequestRules.ExplainToRequester(refusal)
                : null;
        }
        catch (SecretNotFoundException)
        {
            return "That secret no longer exists.";
        }
    }

    /// <summary>Hands off to the identity provider. Nothing is decided here.</summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string? returnUrl)
        => Challenge(
            new AuthenticationProperties { RedirectUri = Url.Action(nameof(ExternalCallback), new { ReturnUrl = returnUrl }) },
            AuthSchemes.Oidc);

    /// <summary>
    /// Back from the identity provider. It has proved who they are; everything about whether they
    /// may in — and whether they are finished authenticating — is the vault's call, made in Core by
    /// <see cref="ISsoSignInService"/> and audited there, refusals included.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalCallback(string? returnUrl, CancellationToken ct)
    {
        var result = await HttpContext.AuthenticateAsync(AuthSchemes.OidcCorrelation);
        // The correlation principal exists only to carry the IdP's answer across this one redirect.
        // Drop it immediately: it is not a session and must never be mistaken for one.
        await HttpContext.SignOutAsync(AuthSchemes.OidcCorrelation);

        if (!result.Succeeded || result.Principal is null)
        {
            return LocalRedirect(SignInPage("/Account/Login", returnUrl, sso: "failed"));
        }

        var identity = ExternalIdentityReader.Read(result.Principal);
        var decision = await _sso.SignInAsync(identity, ct);

        if (decision.Outcome != SsoOutcome.Linked || decision.User is null || decision.Stamp is null)
        {
            // One message for every refusal. The trail records exactly which it was; the sign-in
            // page must not tell an anonymous caller whether an address has an account here.
            _logger.LogWarning("SSO sign-in refused ({Outcome}) for subject {Subject} from {Issuer}",
                decision.Outcome, identity.Subject, identity.Issuer);
            return LocalRedirect(SignInPage("/Account/Login", returnUrl, sso: "refused"));
        }

        var user = decision.User;
        if (decision.SecondFactorSatisfied)
        {
            // The account changed between the identity provider's answer and the session: start again.
            if (!await _issuer.SignInAsync(HttpContext, user, decision.Stamp, new SignInAudit(AuditAction.LoginSucceeded, "Single sign-on"), ct))
            {
                return LocalRedirect(SignInPage("/Account/Login", returnUrl, sso: "failed"));
            }

            return LocalRedirect(SignInTarget.Choose(Url, returnUrl));
        }

        // The IdP proved one factor; this vault wants its own. Rejoin the ordinary flow rather than
        // inventing a second one — TOTP, enrollment and recovery codes all already live there.
        var pending = VaultClaimsFactory.CreatePendingMfaPrincipal(user, decision.Stamp);
        await HttpContext.SignInAsync(AuthSchemes.MfaPending, pending);
        return LocalRedirect(SignInPage(user.TotpEnabled ? "/Account/Totp" : "/Account/EnrollTotp", returnUrl));
    }

    /// <summary>A sign-in page of the Angular UI, carrying the return URL on to be judged when sign-in finishes.</summary>
    private static string SignInPage(string path, string? returnUrl, string? sso = null)
    {
        var query = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(returnUrl))
        {
            query["ReturnUrl"] = returnUrl;
        }

        if (sso is not null)
        {
            query["sso"] = sso;
        }

        return QueryHelpers.AddQueryString(path, query);
    }
}
