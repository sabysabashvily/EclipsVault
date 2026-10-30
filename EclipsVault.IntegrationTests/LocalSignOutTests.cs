using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class LocalSignOutTests(VaultFixture vault)
{
    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task The_local_branch_refuses_every_method_except_get_and_post(string method)
    {
        var response = await vault.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), LocalSignOut.Path));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("/local-sign-out/child")]
    [InlineData("/local-sign-out/")]
    [InlineData("/local-sign-out-extra")]
    public async Task Similar_paths_do_not_enter_the_anonymous_branch(string path)
    {
        var response = await vault.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Local_cookie_clearing_refuses_a_missing_request_token()
    {
        var owner = await vault.SignInNewAccountAsync("it-local-no-token", IPAddress.Parse("192.0.2.90"));

        var response = await owner.Client.PostAsync(LocalSignOut.Path, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(owner.SessionCookie);
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Local_cookie_clearing_refuses_an_invalid_request_token()
    {
        var client = vault.CreateClient();
        await client.GetAsync(LocalSignOut.Path);
        using var clear = new HttpRequestMessage(HttpMethod.Post, LocalSignOut.Path);
        clear.Headers.Add("RequestVerificationToken", "invalid-token");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(clear)).StatusCode);
    }

    [Fact]
    public async Task An_authenticated_pages_token_cannot_authorize_the_anonymous_local_branch()
    {
        var owner = await vault.SignInNewAccountAsync("it-local-bound-token", IPAddress.Parse("192.0.2.91"));
        await owner.Client.GetAsync("/api/ui/me");
        using var clear = new HttpRequestMessage(HttpMethod.Post, LocalSignOut.Path);
        clear.Headers.Add("RequestVerificationToken", owner.Cookies.GetCookies(owner.Client.BaseAddress!)["XSRF-TOKEN"]!.Value);

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.SendAsync(clear)).StatusCode);
        Assert.NotNull(owner.SessionCookie);
    }

    [Fact]
    public async Task An_anonymous_local_token_clears_only_this_browser_and_preserves_the_incomplete_warning()
    {
        var owner = await vault.SignInNewAccountAsync("it-local-only", IPAddress.Parse("192.0.2.92"));
        var copied = vault.CreateSession(owner.SessionCookie, IPAddress.Parse("192.0.2.93"));
        var token = await owner.Client.GetFromJsonAsync<LocalSignOut.TokenResponse>(LocalSignOut.Path);
        using var clear = new HttpRequestMessage(HttpMethod.Post, LocalSignOut.Path);
        clear.Headers.Add("RequestVerificationToken", token!.RequestToken);

        var response = await owner.Client.SendAsync(clear);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("sign_out_incomplete", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null(owner.SessionCookie);
        Assert.Equal(HttpStatusCode.OK, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task A_generation_less_full_cookie_requires_a_fresh_sign_in()
    {
        var owner = await vault.SignInNewAccountAsync("it-no-generation", IPAddress.Parse("192.0.2.94"));
        var oldCookie = WithoutGeneration(owner.SessionCookie!, CookieAuthenticationDefaults.AuthenticationScheme);
        var replay = vault.CreateSession(oldCookie, IPAddress.Parse("192.0.2.95"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Null(replay.SessionCookie);
    }

    [Fact]
    public async Task A_generation_less_pending_cookie_cannot_reach_second_factor_verification()
    {
        const string password = "It-generation-less-pending-2026!";
        await vault.SignInNewAccountAsync("it-no-pending-generation", IPAddress.Parse("192.0.2.96"), password: password);
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.97"));
        await SignInProof.BeginPasswordAsync(pending.Client, "it-no-pending-generation", password);
        var cookie = pending.Cookies.GetCookies(pending.Client.BaseAddress!)["EclipsVault.MfaPending"]!;
        var replay = vault.CreateSession(WithoutGeneration(cookie, AuthSchemes.MfaPending), IPAddress.Parse("192.0.2.98"));

        var response = await replay.Client.GetAsync("/Account/Totp");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.Null(replay.SessionCookie);
    }

    private Cookie WithoutGeneration(Cookie cookie, string scheme)
    {
        var format = vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme).TicketDataFormat;
        var ticket = format.Unprotect(cookie.Value)!;
        var identity = (ClaimsIdentity)ticket.Principal.Identity!;
        identity.RemoveClaim(identity.FindFirst(VaultClaimTypes.CredentialGeneration)!);
        return new Cookie(cookie.Name, format.Protect(ticket));
    }
}
