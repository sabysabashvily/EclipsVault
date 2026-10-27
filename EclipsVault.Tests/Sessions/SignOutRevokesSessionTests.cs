using System.Security.Claims;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Sessions;
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

namespace EclipsVault.Tests.Sessions;

/// <summary>
/// Sign out must end the session on the server, not only forget the cookie in this browser. A copy
/// of the cookie taken before sign-out must lose access on its next request. Revoking the
/// session id is the same tombstone "Sign out all other sessions" uses, so the cookie validator
/// rejects the copy on its next request.
/// </summary>
public class SignOutRevokesSessionTests
{
    private sealed class RecordingAuthentication : IAuthenticationService
    {
        public bool SignedOut { get; private set; }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => throw new NotSupportedException();
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();
    }

    // Reads work, writes fail: Redis at maxmemory with noeviction is the realistic case.
    private sealed class WriteFailingRegistry : ISessionRegistry
    {
        public Task RecordSeenAsync(SessionObservation observation, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, SessionStamp current, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ActiveSession>>([]);
        public Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => throw new InvalidOperationException("OOM command not allowed");
        public Task<bool> TryRetireForReplacementAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> IsRevokedAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private static readonly SessionStamp Stamp = new(0, "current-version");

    private static (AccountController Controller, RecordingAuthentication Auth) SignedInController(
        ISessionRegistry sessions, params Claim[] claims)
    {
        var auth = new RecordingAuthentication();
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(auth).BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };

        // Logout touches only the session registry and the authentication service; the other
        // collaborators belong to the sign-in flows and are never reached here.
        var controller = new AccountController(null!, null!, null!, sessions, null!, null!, null!, null!,
            NullLogger<AccountController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        // RedirectToAction only captures the helper; nothing is routed until the result executes.
        controller.Url = new UrlHelper(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        return (controller, auth);
    }

    [Fact]
    public async Task Signing_out_revokes_the_session_so_a_copied_cookie_is_refused()
    {
        var sessions = new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System);
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await sessions.RecordSeenAsync(new SessionObservation(
            userId, sessionId, Stamp, "Chrome on macOS", "203.0.113.5", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
        var (controller, auth) = SignedInController(sessions,
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(VaultClaimTypes.SessionId, sessionId.ToString()));

        await controller.Logout(CancellationToken.None);

        Assert.True(auth.SignedOut);
        Assert.True(await sessions.IsRevokedAsync(userId, sessionId));
        Assert.Empty(await sessions.ListAsync(userId, Stamp));
    }

    [Fact]
    public async Task A_principal_without_a_session_id_is_still_signed_out()
    {
        var sessions = new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System);
        var (controller, auth) = SignedInController(sessions,
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));

        var result = await controller.Logout(CancellationToken.None);

        Assert.True(auth.SignedOut);
        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task Signing_out_an_unrecorded_session_also_revokes_copied_cookies()
    {
        var sessions = new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System);
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var (controller, auth) = SignedInController(sessions,
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(VaultClaimTypes.SessionId, sessionId.ToString()));

        var result = await controller.Logout(CancellationToken.None);

        Assert.True(auth.SignedOut);
        Assert.True(await sessions.IsRevokedAsync(userId, sessionId));
        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task When_the_revocation_cannot_be_stored_this_browser_is_still_signed_out()
    {
        // Failing closed here would leave a live session in the browser, on a shared machine the
        // likelier threat, so the cookie is cleared regardless and the failure is logged.
        var (controller, auth) = SignedInController(new WriteFailingRegistry(),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(VaultClaimTypes.SessionId, Guid.NewGuid().ToString()));

        var result = await controller.Logout(CancellationToken.None);

        Assert.True(auth.SignedOut);
        Assert.IsType<RedirectToActionResult>(result);
    }
}
