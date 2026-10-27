using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace EclipsVault.IntegrationTests;

public abstract class AccountRevokersTests(VaultFixture vault)
{
    [Fact]
    public async Task Sliding_expiration_preserves_the_original_version_and_cannot_outlive_sign_out()
    {
        var owner = await vault.SignInNewAccountAsync("it-version-sliding", IPAddress.Parse("198.18.240.7"));
        var original = SessionApi.PrincipalOf(vault, owner);
        var format = vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;
        var ticket = format.Unprotect(owner.SessionCookie!.Value)!;
        ticket.Properties.IssuedUtc = DateTimeOffset.UtcNow.AddHours(-7);
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(5);
        var renewing = vault.CreateSession(new Cookie(VaultFixture.SessionCookie, format.Protect(ticket)), IPAddress.Parse("198.18.240.8"));

        var response = await renewing.Client.GetAsync("/api/ui/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(original.GetSessionStampOrNull(), SessionApi.PrincipalOf(vault, renewing).GetSessionStampOrNull());
        Assert.Equal(original.GetSessionId(), SessionApi.PrincipalOf(vault, renewing).GetSessionId());
        await vault.RevokeAllSessionsOfAsync("it-version-sliding");
        Assert.Equal(HttpStatusCode.Unauthorized, (await renewing.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task A_display_name_renewal_after_sign_out_keeps_the_retired_version_and_is_refused()
    {
        var owner = await vault.SignInNewAccountAsync("it-version-late-renewal", IPAddress.Parse("198.18.240.9"));
        var original = SessionApi.PrincipalOf(vault, owner);
        var account = await LegacyProfile.AccountAsync(vault, "it-version-late-renewal");
        var token = VaultFixture.FormTokenOf(await owner.Client.GetStringAsync("/Profile"), "/Profile");
        await using var body = new GatedRequestBody("__RequestVerificationToken=" + Uri.EscapeDataString(token)
            + "&DisplayName=Renamed%20Owner&Email=" + Uri.EscapeDataString(account.Email));
        var request = vault.SendAsync(context =>
        {
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = "/Profile/Update";
            context.Request.Method = "POST";
            context.Request.Headers.Cookie = owner.Cookies.GetCookieHeader(owner.Client.BaseAddress!);
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.ContentLength = body.Length;
            context.Request.Body = body;
            context.Connection.RemoteIpAddress = IPAddress.Parse("198.18.240.9");
        });
        try
        {
            await body.ReadStarted.WaitAsync(TimeSpan.FromSeconds(30));
            await vault.Services.GetRequiredService<ISessionRevocationService>().RevokeAsync(account.Id);
        }
        finally
        {
            body.Release();
        }
        var response = await request;
        Assert.Equal(StatusCodes.Status302Found, response.Response.StatusCode);
        var cookie = SetCookieHeaderValue.Parse(response.Response.Headers.SetCookie
            .Single(value => value!.StartsWith(VaultFixture.SessionCookie + "=", StringComparison.Ordinal))!);
        var renewed = vault.CreateSession(new Cookie(cookie.Name.ToString(), cookie.Value.ToString()), IPAddress.Parse("198.18.240.10"));

        Assert.Equal(original.GetSessionStampOrNull(), SessionApi.PrincipalOf(vault, renewed).GetSessionStampOrNull());
        Assert.Equal(HttpStatusCode.Unauthorized, (await renewed.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task A_secret_step_up_renewal_preserves_the_original_version_and_cannot_outlive_sign_out()
    {
        const string username = "it-version-step-up";
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-version-step-up-secret", "it-step-up-value");
        // A trusted address, so the reveal depends on the session alone.
        var owner = await vault.SignInNewAccountAsync(username, IPAddress.Parse("10.52.0.73"));
        var original = SessionApi.PrincipalOf(vault, owner);
        var originalCookie = owner.SessionCookie!.Value;
        var account = await LegacyProfile.AccountAsync(vault, username);
        var token = VaultFixture.FormTokenOf(await owner.Client.GetStringAsync(details), details);

        var response = await owner.Client.PostAsync(details.Replace("/Details/", "/StepUpReveal/", StringComparison.Ordinal),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["code"] = Totp.NextCode(account.TotpSecret!),
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("it-step-up-value", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.NotEqual(originalCookie, owner.SessionCookie!.Value);
        var renewed = SessionApi.PrincipalOf(vault, owner);
        Assert.Equal(original.GetSessionStampOrNull(), renewed.GetSessionStampOrNull());
        Assert.Equal(original.GetSessionId(), renewed.GetSessionId());
        await vault.RevokeAllSessionsOfAsync(username);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task A_role_change_retires_the_original_version_and_refuses_the_old_cookie()
    {
        var owner = await vault.SignInNewAccountAsync("it-version-role", IPAddress.Parse("198.18.240.1"));
        var original = SessionApi.PrincipalOf(vault, owner);
        using var request = vault.Services.CreateScope();

        await request.ServiceProvider.GetRequiredService<IUserAdminService>().SetRoleAsync(original.GetUserId(), ClearanceLevel.Elevated, "OTHER", default);

        Assert.NotEqual(original.GetSessionStampOrNull()!.RevocationVersion,
            await vault.Services.GetRequiredService<ISessionRevocationService>().FindAsync(original.GetUserId()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task An_administrator_authenticator_reset_refuses_the_accounts_sessions()
    {
        var owner = await vault.SignInNewAccountAsync("it-version-admin-reset", IPAddress.Parse("198.18.240.11"));
        var original = SessionApi.PrincipalOf(vault, owner);
        using var request = vault.Services.CreateScope();

        await request.ServiceProvider.GetRequiredService<IUserAdminService>().ResetTotpAsync(original.GetUserId(), default);

        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Disabling_an_account_retires_its_original_version()
    {
        var owner = await vault.SignInNewAccountAsync("it-version-disable", IPAddress.Parse("198.18.240.2"));
        var original = SessionApi.PrincipalOf(vault, owner);
        using var request = vault.Services.CreateScope();

        await request.ServiceProvider.GetRequiredService<IUserAdminService>().SetEnabledAsync(original.GetUserId(), false, default);

        Assert.NotEqual(original.GetSessionStampOrNull()!.RevocationVersion,
            await vault.Services.GetRequiredService<ISessionRevocationService>().FindAsync(original.GetUserId()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Forced_logout_retires_the_original_version()
    {
        var owner = await vault.SignInNewAccountAsync("it-version-forced", IPAddress.Parse("198.18.240.3"));
        var original = SessionApi.PrincipalOf(vault, owner);

        await vault.RevokeAllSessionsOfAsync("it-version-forced");

        Assert.NotEqual(original.GetSessionStampOrNull()!.RevocationVersion,
            await vault.Services.GetRequiredService<ISessionRevocationService>().FindAsync(original.GetUserId()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Account_deletion_retires_the_original_version()
    {
        var owner = await vault.SignInNewAccountAsync("it-version-deleted", IPAddress.Parse("198.18.240.4"));
        var original = SessionApi.PrincipalOf(vault, owner);

        await vault.DeleteAccountAsync("it-version-deleted");

        Assert.NotEqual(original.GetSessionStampOrNull()!.RevocationVersion,
            await vault.Services.GetRequiredService<ISessionRevocationService>().FindAsync(original.GetUserId()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Intrusion_response_retires_the_original_version_even_for_a_copied_cookie_on_another_network()
    {
        var owner = await vault.SignInNewAccountAsync("it-version-intrusion", IPAddress.Parse("198.18.240.5"));
        var original = SessionApi.PrincipalOf(vault, owner);
        using var request = vault.Services.CreateScope();
        var accessor = request.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = original };
        accessor.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("198.19.255.30");
        try
        {
            await request.ServiceProvider.GetRequiredService<IIntrusionResponseService>()
                .TriggerHoneyTokenAsync(Guid.NewGuid(), "Disposable revocation probe", default);
        }
        finally
        {
            accessor.HttpContext = null;
        }

        Assert.NotEqual(original.GetSessionStampOrNull()!.RevocationVersion,
            await vault.Services.GetRequiredService<ISessionRevocationService>().FindAsync(original.GetUserId()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Demotion_between_verification_and_issuance_refuses_the_stale_role_snapshot()
    {
        const string password = "It-role-issuance-password-2026!";
        await vault.SignInNewAccountAsync("it-version-issuance", IPAddress.Parse("198.18.240.6"), password: password);
        var account = await LegacyProfile.AccountAsync(vault, "it-version-issuance");
        using var proofRequest = vault.Services.CreateScope();
        var authentication = proofRequest.ServiceProvider.GetRequiredService<IVaultAuthenticationService>();
        var passwordStage = await authentication.ValidateCredentialsAsync(account.Username, password, default);
        var proven = await authentication.VerifyTotpAsync(account.Id, passwordStage.Stamp!, Totp.NextCode(account.TotpSecret!), default);
        using (var administration = vault.Services.CreateScope())
        {
            await administration.ServiceProvider.GetRequiredService<IUserAdminService>()
                .SetRoleAsync(account.Id, ClearanceLevel.Standard, "RESTRICTED", default);
        }
        var http = new DefaultHttpContext { RequestServices = proofRequest.ServiceProvider };

        Assert.False(await proofRequest.ServiceProvider.GetRequiredService<SessionIssuer>().SignInAsync(http, proven!, passwordStage.Stamp!));

        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
    }
}

[Collection(VaultCollection.Name)]
public sealed class MemoryAccountRevokersTests(VaultFixture vault) : AccountRevokersTests(vault);

[Collection(RedisVaultCollection.Name)]
public sealed class RedisAccountRevokersTests(RedisVaultFixture fixture) : AccountRevokersTests(fixture.Vault);
