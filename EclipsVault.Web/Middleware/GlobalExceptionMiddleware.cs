using EclipsVault.Core.Domain.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EclipsVault.Web.Middleware;

/// <summary>
/// Maps domain exceptions to safe HTTP outcomes. Honey-token trips look like an
/// ordinary sign-out + not-found to the attacker; audit failures surface as a 503
/// so callers know the vault chose fail-closed over fail-open. The Legacy UI is
/// redirected to an error page; the UI API gets the same status as Problem Details
/// with a stable code (ADR-0006), and neither ever carries the exception's details.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (HoneyTokenTrippedException ex) when (!context.Response.HasStarted)
        {
            // Intrusion response already executed inside the service layer.
            _logger.LogWarning("Honey-token {SecretName} tripped during {Path}; terminating session", ex.SecretName, context.Request.Path);
            if (UiApiProblem.IsUiApi(context.Request))
            {
                // Exactly an ordinary not-found, headers included: the answer must not tell the intruder
                // the trap fired. So the cookie is not deleted here (a sign-out would add Set-Cookie);
                // the session is already revoked, and its next request gets the 401 that clears it.
                await UiApiProblem.NotFoundAsync(context);
            }
            else
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.Response.Redirect("/Account/Login");
            }
        }
        catch (AuditWriteFailedException ex) when (!context.Response.HasStarted)
        {
            _logger.LogCritical(ex, "Fail-closed: request to {Path} aborted because the audit trail is unavailable", context.Request.Path);
            await FailAsync(context, StatusCodes.Status503ServiceUnavailable, "audit_unavailable", "The audit trail is unavailable, so the vault refused the request.");
        }
        catch (SessionStoreUnavailableException ex) when (!context.Response.HasStarted)
        {
            _logger.LogCritical(ex, "Fail-closed: request to {Path} aborted because the session store is unavailable", context.Request.Path);
            await FailAsync(context, StatusCodes.Status503ServiceUnavailable, "session_store_unavailable", "The session store is unavailable, so the vault refused the change. Nothing was saved.");
        }
        catch (SecretNotFoundException ex) when (!context.Response.HasStarted)
        {
            _logger.LogInformation("Secret {SecretId} not found for {Path}", ex.SecretId, context.Request.Path);
            await FailAsync(context, StatusCodes.Status404NotFound, UiApiProblem.NotFoundCode, UiApiProblem.NotFoundTitle);
        }
        catch (LegacyBlobRefusedException ex) when (!context.Response.HasStarted)
        {
            // A deliberate fail-closed refusal (an unbound legacy envelope), not a fault: report it as
            // a clean 409 the operator can act on, not a 500 that reads like the crypto is broken.
            _logger.LogWarning(ex, "Refused an unbound legacy secret during {Path}; a re-seal migration is required", context.Request.Path);
            await FailAsync(context, StatusCodes.Status409Conflict, "legacy_blob_refused", "This secret is sealed in a format the vault refuses until an administrator re-seals it.");
        }
        catch (CryptoConfigurationException ex) when (!context.Response.HasStarted)
        {
            _logger.LogCritical(ex, "Cryptographic subsystem misconfiguration surfaced during {Path}", context.Request.Path);
            await FailAsync(context, StatusCodes.Status500InternalServerError, "crypto_unavailable", "The vault's encryption is unavailable.");
        }
        catch (PremiumFeatureNotLicensedException ex) when (!context.Response.HasStarted)
        {
            // A payment / entitlement boundary, not a fault: 402, not 400/500. Existing state is intact.
            _logger.LogInformation("Refused unlicensed premium feature '{Feature}' during {Path}", ex.FeatureKey, context.Request.Path);
            await FailAsync(context, StatusCodes.Status402PaymentRequired, "premium_not_licensed", "This feature needs a licence.");
        }
        catch (DomainException ex) when (!context.Response.HasStarted)
        {
            _logger.LogError(ex, "Domain error during {Path}", context.Request.Path);
            await FailAsync(context, StatusCodes.Status400BadRequest, "invalid_request", "The request could not be completed.");
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            _logger.LogError(ex, "Unhandled exception during {Path}", context.Request.Path);
            await FailAsync(context, StatusCodes.Status500InternalServerError, "internal_error", "Something went wrong.");
        }
    }

    /// <summary>Problem Details on the UI API; everywhere else the Legacy UI's error page for the same status.</summary>
    private static Task FailAsync(HttpContext context, int status, string code, string title)
    {
        if (UiApiProblem.IsUiApi(context.Request))
        {
            return UiApiProblem.WriteAsync(context, status, code, title);
        }

        context.Response.Redirect($"/Home/Error?code={status}");
        return Task.CompletedTask;
    }
}
