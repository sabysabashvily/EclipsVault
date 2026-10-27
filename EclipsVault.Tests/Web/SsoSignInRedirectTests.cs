using System.Security.Claims;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Application.Sso;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Security;
using EclipsVault.Tests.Sessions;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.Web;

/// <summary>
/// When the identity provider satisfied both factors, SSO completes the session in its callback.
/// Its default landing page is the dashboard;
/// a return target replaces that default only when it is one the password flow would accept too.
/// SSO needs a live identity provider, so this drives the callback directly rather than through
/// the integration host.
/// </summary>
public class SsoSignInRedirectTests
{
    private sealed class AcceptingAuthentication : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "idp-subject")], scheme)), scheme!)));

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
    }

    private sealed class BothFactorsAtTheIdp(Guid accountId, string revocationVersion) : ISsoSignInService
    {
        public Task<SsoSignInResult> SignInAsync(ExternalIdentity identity, CancellationToken ct) =>
            Task.FromResult(SsoSignInResult.Linked(new UserDto(accountId, "sso-user", "SSO User",
                "sso@example.test", ClearanceLevel.Standard, "GLOBAL", TotpEnabled: false, IsDisabled: false),
                new SessionStamp(0, revocationVersion), true));
    }

    // Conventional routes resolve to /Controller; local-URL checks are the framework's own.
    private sealed class ConventionalUrls(ActionContext context) : UrlHelperBase(context)
    {
        public override string? Action(UrlActionContext action) => "/" + action.Controller;
        public override string? RouteUrl(UrlRouteContext route) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(null, "/Dashboard")]
    [InlineData("https://outside.example/", "/Dashboard")]
    [InlineData("/Account/Totp", "/Dashboard")]
    [InlineData("/Access", "/Access")]
    public async Task Sso_that_satisfied_both_factors_lands_on_the_dashboard_unless_given_a_local_target(string? returnUrl, string expected)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IAuthenticationService>(new AcceptingAuthentication())
                .BuildServiceProvider()
        };
        var accountId = Guid.NewGuid();
        var revocation = new InMemorySessionRevocationService(NullLogger<InMemorySessionRevocationService>.Instance);
        var sessions = new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System);
        var issuer = new SessionIssuer(new SessionAccountStore(new User { Id = accountId }), revocation, sessions, NullLogger<SessionIssuer>.Instance);
        var controller = new AccountController(null!, null!, null!, sessions, null!,
            new BothFactorsAtTheIdp(accountId, await revocation.CaptureAsync(accountId)), null!, issuer,
            NullLogger<AccountController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        controller.Url = new ConventionalUrls(new ActionContext(http, new RouteData(), new ActionDescriptor()));

        var result = await controller.ExternalCallback(returnUrl, CancellationToken.None);

        Assert.Equal(expected, Assert.IsType<LocalRedirectResult>(result).Url);
    }
}
