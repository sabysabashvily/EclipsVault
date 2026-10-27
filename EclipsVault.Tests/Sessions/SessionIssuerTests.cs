using System.Security.Claims;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Security;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.Sessions;

public sealed class SessionIssuerTests
{
    [Fact]
    public async Task A_sign_in_cannot_issue_a_session_after_its_proven_generation_changed()
    {
        var account = new User { Id = Guid.NewGuid(), CredentialGeneration = 2 };
        var revocation = NewRevocation();
        var stamp = new SessionStamp(1, await revocation.CaptureAsync(account.Id));
        var authentication = new RecordingAuthentication();
        var http = Context(authentication);
        var issuer = Issuer(account, revocation);

        Assert.False(await issuer.SignInAsync(http, UserDto.From(account), stamp));

        Assert.Null(authentication.Issued);
    }

    [Fact]
    public async Task Revocation_after_loading_a_role_snapshot_prevents_issuing_it_under_a_newer_version()
    {
        var account = new User { Id = Guid.NewGuid() };
        var revocation = NewRevocation();
        var stamp = new SessionStamp(0, await revocation.CaptureAsync(account.Id));
        var snapshot = UserDto.From(account);
        account.ProjectKey = "RESTRICTED";
        await revocation.RevokeAsync(account.Id);
        var authentication = new RecordingAuthentication();

        Assert.False(await Issuer(account, revocation).SignInAsync(Context(authentication), snapshot, stamp));

        Assert.Null(authentication.Issued);
    }

    [Fact]
    public async Task A_failed_replacement_cookie_reports_sign_in_required_and_keeps_the_committed_generation()
    {
        var account = new User { Id = Guid.NewGuid(), CredentialGeneration = 1 };
        var revocation = NewRevocation();
        var stamp = new SessionStamp(0, await revocation.CaptureAsync(account.Id));
        var authentication = new RecordingAuthentication { RejectSignIn = true };
        var http = Context(authentication);
        http.User = VaultClaimsFactory.CreateSessionPrincipal(UserDto.From(account), Guid.NewGuid(), DateTimeOffset.UtcNow, stamp);

        Assert.False(await Issuer(account, revocation).ReplaceAsync(http, 1));

        Assert.True(authentication.SignedOut);
        Assert.Equal(1, account.CredentialGeneration);
        Assert.Null(authentication.Issued);
    }

    [Fact]
    public async Task A_second_credential_change_prevents_replacing_a_session_under_the_first_changes_generation()
    {
        var account = new User { Id = Guid.NewGuid(), CredentialGeneration = 2 };
        var revocation = NewRevocation();
        var stamp = new SessionStamp(0, await revocation.CaptureAsync(account.Id));
        var authentication = new RecordingAuthentication();
        var http = Context(authentication);
        http.User = VaultClaimsFactory.CreateSessionPrincipal(UserDto.From(account), Guid.NewGuid(), DateTimeOffset.UtcNow, stamp);

        Assert.False(await Issuer(account, revocation).ReplaceAsync(http, 1));

        Assert.True(authentication.SignedOut);
        Assert.Null(authentication.Issued);
    }

    [Fact]
    public async Task A_replacement_uses_a_new_id_and_keeps_the_original_external_version()
    {
        var account = new User { Id = Guid.NewGuid(), CredentialGeneration = 1 };
        var revocation = NewRevocation();
        var stamp = new SessionStamp(0, await revocation.CaptureAsync(account.Id));
        var authentication = new RecordingAuthentication();
        var http = Context(authentication);
        var oldId = Guid.NewGuid();
        http.User = VaultClaimsFactory.CreateSessionPrincipal(UserDto.From(account), oldId, DateTimeOffset.UtcNow, stamp);

        Assert.True(await Issuer(account, revocation).ReplaceAsync(http, 1));

        Assert.NotEqual(oldId, authentication.Issued!.GetSessionId());
        Assert.Equal(new SessionStamp(1, stamp.RevocationVersion), authentication.Issued!.GetSessionStampOrNull());
    }

    private static InMemorySessionRevocationService NewRevocation()
        => new(NullLogger<InMemorySessionRevocationService>.Instance);

    private static SessionIssuer Issuer(User account, InMemorySessionRevocationService revocation)
        => new(new SessionAccountStore(account), revocation,
            new InMemorySessionRegistry(NullLogger<InMemorySessionRegistry>.Instance, TimeProvider.System),
            NullLogger<SessionIssuer>.Instance);

    private static DefaultHttpContext Context(RecordingAuthentication authentication)
        => new()
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider(),
        };

    private sealed class RecordingAuthentication : IAuthenticationService
    {
        public bool RejectSignIn { get; init; }
        public bool SignedOut { get; private set; }
        public ClaimsPrincipal? Issued { get; private set; }

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            if (RejectSignIn)
            {
                throw new InvalidOperationException("The cookie could not be issued.");
            }
            Issued = principal;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => throw new NotSupportedException();
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
    }
}
