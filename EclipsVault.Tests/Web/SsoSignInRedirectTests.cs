using System.Security.Claims;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Sso;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Security;
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
/// Its default landing page has always been the dashboard, not the password flow's secrets list;
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

    private sealed class BothFactorsAtTheIdp : ISsoSignInService
    {
        public Task<SsoSignInResult> SignInAsync(ExternalIdentity identity, CancellationToken ct) =>
            Task.FromResult(SsoSignInResult.Linked(new UserDto(Guid.NewGuid(), "sso-user", "SSO User",
                "sso@example.test", ClearanceLevel.Standard, "GLOBAL", TotpEnabled: false, IsDisabled: false), true));
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
        var controller = new AccountController(null!, null!, null!,
            new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance), null!,
            new BothFactorsAtTheIdp(), null!, NullLogger<AccountController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        controller.Url = new ConventionalUrls(new ActionContext(http, new RouteData(), new ActionDescriptor()));

        var result = await controller.ExternalCallback(returnUrl, CancellationToken.None);

        Assert.Equal(expected, Assert.IsType<LocalRedirectResult>(result).Url);
    }
}
