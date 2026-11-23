using System.Security.Claims;
using System.Text.Encodings.Web;
using EclipsVault.Core.Application.ServiceAccounts;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EclipsVault.Web.Authentication;

/// <summary>
/// Authenticates non-interactive API callers from an <c>Authorization: Bearer evk_…</c>
/// or <c>X-Api-Key: evk_…</c> header. A valid key yields a principal carrying the
/// service account's vault attribute claims, so the very same ABAC policy that governs
/// interactive users also governs API access.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IApiKeyAuthenticator _authenticator;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyAuthenticator authenticator)
        : base(options, logger, encoder)
    {
        _authenticator = authenticator;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ExtractToken();
        if (token is null)
        {
            return AuthenticateResult.NoResult(); // no credentials presented
        }

        var account = await _authenticator.AuthenticateAsync(token, Context.Connection.RemoteIpAddress, Context.RequestAborted);
        if (account is null)
        {
            return AuthenticateResult.Fail("Invalid, expired, revoked, or disabled API key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.Name),
            new(VaultClaimTypes.Display, account.Name),
            // Effective clearance — already capped by any per-key ceiling.
            new(VaultClaimTypes.Clearance, ((int)account.Clearance).ToString()),
            new(VaultClaimTypes.Project, account.ProjectKey),
            new(VaultClaimTypes.ActorType, "service")
        };

        // Per-key scope travels as claims so the same ABAC handler enforces it.
        if (!string.IsNullOrEmpty(account.ProjectScope))
        {
            claims.Add(new Claim(VaultClaimTypes.ScopeProject, account.ProjectScope));
        }
        if (account.MetadataOnly)
        {
            claims.Add(new Claim(VaultClaimTypes.ScopeMetadataOnly, "true"));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        => PublicApiError.WriteAsync(Context, StatusCodes.Status401Unauthorized, "unauthenticated");

    private string? ExtractToken()
    {
        if (Request.Headers.TryGetValue("X-Api-Key", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey.ToString().Trim();
        }

        var authorization = Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : null;
    }
}
