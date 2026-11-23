using System.Globalization;
using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The surface on the Production host's endpoint table (ADR-0034), not only on controller actions:
/// route-table pages and fallbacks are endpoints too. Each endpoint is classified by the policy the
/// authorization middleware would combine for it. Adding a name to a list here is a decision about
/// who may reach that endpoint.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class EndpointTableTests(VaultFixture vault)
{
    /// <summary>
    /// Reachable with no session: sign-in's pages and calls, account activation's page and calls for the
    /// holder of an emailed invitation (ADR-0033), single sign-on's two redirects, and the Public API's
    /// not-found, which says nothing about any resource.
    /// </summary>
    private static readonly string[] Anonymous =
    [
        "GET /Account/ExternalCallback",
        "GET,HEAD /Account/Activate",
        "GET,HEAD /Account/Login",
        "GET,HEAD /Account/Recover",
        "POST /Account/ExternalLogin",
        "GET /api/ui/auth/options",
        "POST /api/ui/auth/activation",
        "POST /api/ui/auth/activation/complete",
        "POST /api/ui/auth/passkey/begin",
        "POST /api/ui/auth/passkey/complete",
        "POST /api/ui/auth/password",
        "POST /api/ui/auth/recover",
        "* /api/v1/{**path}",
    ];

    /// <summary>The second-factor pages and calls, which accept only the pending sign-in.</summary>
    private static readonly string[] PendingSignIn =
    [
        "GET,HEAD /Account/EnrollTotp",
        "GET,HEAD /Account/RecoveryCode",
        "GET,HEAD /Account/Totp",
        "POST /api/ui/auth/enrollment",
        "POST /api/ui/auth/enrollment/confirm",
        "POST /api/ui/auth/recovery-code",
        "POST /api/ui/auth/totp",
    ];

    /// <summary>The route table's administrator pages.</summary>
    private static readonly string[] AdministratorPages =
    [
        "GET,HEAD /Admin/CreateUser",
        "GET,HEAD /Admin/EditUser/{id:guid}",
        "GET,HEAD /Admin/Networks",
        "GET,HEAD /Admin/Users",
        "GET,HEAD /Audit",
        "GET,HEAD /Audit/Index",
        "GET,HEAD /Encryption",
        "GET,HEAD /Encryption/Index",
        "GET,HEAD /License",
        "GET,HEAD /License/Index",
        "GET,HEAD /Notifications",
        "GET,HEAD /Notifications/Index",
        "GET,HEAD /ServiceAccounts",
        "GET,HEAD /ServiceAccounts/Create",
        "GET,HEAD /ServiceAccounts/Details/{id:guid}",
        "GET,HEAD /ServiceAccounts/Index",
    ];

    [Fact]
    public async Task The_anonymous_endpoints_are_exactly_the_intended_ones()
        => Assert.Equal(Anonymous.Order(StringComparer.Ordinal), await NamesAsync(Kind.Anonymous));

    [Fact]
    public async Task Only_the_second_factor_endpoints_accept_the_pending_sign_in()
        => Assert.Equal(PendingSignIn.Order(StringComparer.Ordinal), await NamesAsync(Kind.PendingSignIn));

    [Fact]
    public async Task The_administrator_pages_are_exactly_the_route_tables()
        => Assert.Equal(AdministratorPages.Order(StringComparer.Ordinal), await NamesAsync(Kind.AdministratorPage));

    [Fact]
    public async Task Public_API_endpoints_accept_an_API_key_and_nothing_else()
    {
        var endpoints = await ClassifyAsync();
        var publicApi = endpoints.Where(endpoint => endpoint.Name.Contains(" /api/v1/", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(publicApi);
        Assert.All(publicApi.Where(endpoint => endpoint.Kind != Kind.Anonymous),
            endpoint => Assert.Equal(Kind.ApiKey, endpoint.Kind));
        Assert.All(endpoints.Where(endpoint => endpoint.Kind == Kind.ApiKey),
            endpoint => Assert.Contains(" /api/v1/", endpoint.Name, StringComparison.Ordinal));
    }

    /// <summary>Everything not named above needs a session, the page not-found and the UI API's included.</summary>
    [Fact]
    public async Task Every_other_endpoint_requires_a_session()
    {
        var endpoints = await ClassifyAsync();

        Assert.Empty(endpoints.Where(endpoint => endpoint.Kind == Kind.Other).Select(endpoint => endpoint.Name));
        var session = endpoints.Where(endpoint => endpoint.Kind is Kind.Session or Kind.AdministratorPage)
            .Select(endpoint => endpoint.Name).ToList();
        Assert.Contains("GET,HEAD /", session);
        Assert.Contains("* /{**path:nonfile}", session);
        Assert.Contains("* /Notifications", session);
        Assert.Contains("* /api/ui/{**path}", session);
        Assert.Contains("GET /api/ui/me", session);
    }

    [Fact]
    public async Task An_unknown_public_API_path_is_a_JSON_not_found_without_a_session_challenge()
    {
        using var anonymous = await vault.CreateClient().GetAsync("/api/v1/x");
        using var signedIn = await (await vault.AdminClientAsync()).PostAsync("/api/v1/no/such/path", content: null);

        foreach (var response in new[] { anonymous, signedIn })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Empty(response.Headers.WwwAuthenticate);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("""{"error":"not_found"}""", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Single_sign_on_keeps_its_two_urls()
    {
        // Routed and anonymous: the antiforgery check answers before the identity provider is asked.
        using var login = await vault.CreateClient().PostAsync("/Account/ExternalLogin", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);

        using var callback = await vault.CreateClient().GetAsync("/Account/ExternalCallback");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/Account/Login?sso=failed", callback.Headers.Location?.OriginalString);
    }

    private enum Kind
    {
        Anonymous,
        PendingSignIn,
        AdministratorPage,
        Session,
        ApiKey,
        Other,
    }

    private sealed record Classified(string Name, Kind Kind);

    private async Task<List<string>> NamesAsync(Kind kind)
        => (await ClassifyAsync()).Where(endpoint => endpoint.Kind == kind)
            .Select(endpoint => endpoint.Name).Order(StringComparer.Ordinal).ToList();

    private async Task<List<Classified>> ClassifyAsync()
    {
        using var scope = vault.Services.CreateScope();
        var policies = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var classified = new List<Classified>();
        foreach (var endpoint in vault.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            // The fixture's test-only endpoints come from this assembly; the vault never loads them.
            if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Assembly == typeof(VaultFixture).Assembly)
            {
                continue;
            }

            classified.Add(new Classified(NameOf(endpoint), await KindOfAsync(endpoint, policies)));
        }
        return classified;
    }

    private static string NameOf(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        var pattern = "/" + endpoint.RoutePattern.RawText!.TrimStart('/');
        return $"{(methods is { Count: > 0 } ? string.Join(",", methods) : "*")} {pattern}";
    }

    /// <summary>The policy the authorization middleware combines for the endpoint, as it would.</summary>
    private static async Task<Kind> KindOfAsync(Endpoint endpoint, IAuthorizationPolicyProvider policies)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return Kind.Anonymous;
        }

        var policy = await AuthorizationPolicy.CombineAsync(policies,
                         endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                         endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>())
                     ?? await policies.GetFallbackPolicyAsync();
        if (policy is null || !policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Any())
        {
            return Kind.Other;
        }

        // No scheme named means the default one, the session cookie.
        string[] schemes = policy.AuthenticationSchemes.Count == 0
            ? [CookieAuthenticationDefaults.AuthenticationScheme]
            : [.. policy.AuthenticationSchemes.Distinct(StringComparer.Ordinal)];
        return schemes switch
        {
            [AuthSchemes.MfaPending] => Kind.PendingSignIn,
            [AuthSchemes.ApiKey] => Kind.ApiKey,
            [CookieAuthenticationDefaults.AuthenticationScheme] when IsAdministratorPage(endpoint, policy) => Kind.AdministratorPage,
            [CookieAuthenticationDefaults.AuthenticationScheme] => Kind.Session,
            _ => Kind.Other,
        };
    }

    private static bool IsAdministratorPage(Endpoint endpoint, AuthorizationPolicy policy)
        => endpoint is RouteEndpoint route
           && !route.RoutePattern.RawText!.TrimStart('/').StartsWith("api/", StringComparison.OrdinalIgnoreCase)
           && policy.Requirements.OfType<ClaimsAuthorizationRequirement>()
               .Any(requirement => requirement.ClaimType == VaultClaimTypes.Clearance
                   && requirement.AllowedValues?.SequenceEqual([((int)ClearanceLevel.TopSecret).ToString(CultureInfo.InvariantCulture)]) == true);
}
