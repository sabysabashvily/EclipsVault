using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// CSRF for the Angular UI (ADR-0004) and the sign-out it drives. <c>/api/ui/me</c> hands the
/// antiforgery request token to Angular in a readable cookie, Angular echoes it in the header
/// antiforgery already accepts, and sign-out ends the session on the server.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class CsrfAndSignOutTests(VaultFixture vault)
{
    private const string XsrfCookie = "XSRF-TOKEN";
    private const string TokenHeader = "RequestVerificationToken";

    [Fact]
    public async Task Me_sets_a_fresh_readable_secure_strict_cookie_on_every_call()
    {
        var admin = await vault.AdminClientAsync();

        var first = XsrfCookieOf(await admin.GetAsync("/api/ui/me"));
        var second = XsrfCookieOf(await admin.GetAsync("/api/ui/me"));

        Assert.All(new[] { first, second }, cookie =>
        {
            Assert.True(cookie.Secure, "The cookie is not Secure");
            Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
            Assert.False(cookie.HttpOnly, "Angular cannot read an HttpOnly cookie");
            Assert.Equal("/", cookie.Path.ToString());
        });
        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public async Task The_cookie_holds_no_session()
    {
        var token = XsrfCookieOf(await (await vault.AdminClientAsync()).GetAsync("/api/ui/me")).Value.ToString();
        var client = vault.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ui/me");
        request.Headers.Add("Cookie", $"{XsrfCookie}={token}; {VaultFixture.SessionCookie}={token}");
        var response = await client.SendAsync(request);

        await UiApiFailureContractTests.AssertProblemAsync(response, 401, "unauthenticated");
    }

    [Fact]
    public async Task An_unsafe_ui_api_request_without_the_header_is_a_problem_and_with_it_succeeds()
    {
        var admin = await vault.AdminClientAsync();
        var token = XsrfCookieOf(await admin.GetAsync("/api/ui/me")).Value.ToString();

        var refused = await admin.PostAsync("/api/ui/test-only/unsafe", content: null);
        var accepted = await admin.SendAsync(PostWithToken("/api/ui/test-only/unsafe", token));

        await UiApiFailureContractTests.AssertProblemAsync(refused, 400, "antiforgery_rejected");
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    public async Task Sign_out_needs_the_header_and_then_the_old_session_cookie_gets_a_401()
    {
        // TEST-NET-2 (RFC 5737): its own address, so this sign-in spends no other test's sign-in budget.
        var session = await vault.SignInNewAccountAsync("it-sign-out", IPAddress.Parse("198.51.100.50"));
        var copied = Assert.IsType<Cookie>(session.SessionCookie);
        var token = XsrfCookieOf(await session.Client.GetAsync("/api/ui/me")).Value.ToString();

        var refused = await session.Client.PostAsync("/Account/Logout", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/ui/me")).StatusCode);

        var invalid = await session.Client.SendAsync(PostWithToken("/Account/Logout", "invalid-antiforgery-token"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/ui/me")).StatusCode);

        var signedOut = await session.Client.SendAsync(PostWithToken("/Account/Logout", token));
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Null(session.SessionCookie);

        // The browser dropped its cookie, but a copy of it must be dead on the server too.
        var replayed = await vault.CreateSession(copied).Client.GetAsync("/api/ui/me");
        await UiApiFailureContractTests.AssertProblemAsync(replayed, 401, "unauthenticated");
    }

    [Fact]
    public async Task An_exhausted_shared_credential_budget_cannot_prevent_protected_sign_out()
    {
        var source = IPAddress.Parse("198.51.100.91");
        var session = await vault.SignInNewAccountAsync("it-throttled-sign-out", source);
        var copied = Assert.IsType<Cookie>(session.SessionCookie);
        var token = XsrfCookieOf(await session.Client.GetAsync("/api/ui/me")).Value.ToString();

        // An anonymous caller behind the same address spends the real shared credential budget.
        // Empty login models reach the throttle without guessing a password or tripping lockout.
        var guesser = vault.CreateSession(source: source.MapToIPv6()).Client;
        var guessToken = VaultFixture.FormTokenOf(await guesser.GetStringAsync("/Account/Login"), "/Account/Login");
        for (var i = 0; i < PermitLimit; i++)
        {
            await guesser.SendAsync(PostWithToken("/Account/Login", guessToken));
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await guesser.SendAsync(PostWithToken("/Account/Login", guessToken))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await session.Client.PostAsync("/Account/Logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.Client.SendAsync(PostWithToken("/Account/Logout", "invalid"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await vault.CreateSession(copied).Client.GetAsync("/api/ui/me")).StatusCode);

        var signedOut = await session.Client.SendAsync(PostWithToken("/Account/Logout", token));
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal("/", signedOut.Headers.Location?.OriginalString);
        Assert.Null(session.SessionCookie);
        await UiApiFailureContractTests.AssertProblemAsync(
            await vault.CreateSession(copied).Client.GetAsync("/api/ui/me"), 401, "unauthenticated");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await guesser.SendAsync(PostWithToken("/Account/Login", guessToken))).StatusCode);
    }

    [Fact]
    public async Task Protected_sign_out_leaves_the_last_credential_attempt_available()
    {
        // Copy a genuinely authenticated session to a fresh source with an untouched budget.
        var original = await vault.SignInNewAccountAsync("it-sign-out-budget", IPAddress.Parse("198.51.100.92"));
        var session = vault.CreateSession(Assert.IsType<Cookie>(original.SessionCookie), IPAddress.Parse("198.51.100.93"));
        var guesser = vault.CreateSession(source: IPAddress.Parse("198.51.100.93")).Client;
        var guessToken = VaultFixture.FormTokenOf(await guesser.GetStringAsync("/Account/Login"), "/Account/Login");
        for (var i = 0; i < PermitLimit - 1; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await guesser.SendAsync(PostWithToken("/Account/Login", guessToken))).StatusCode);
        }
        var token = XsrfCookieOf(await session.Client.GetAsync("/api/ui/me")).Value.ToString();

        Assert.Equal(HttpStatusCode.Redirect, (await session.Client.SendAsync(PostWithToken("/Account/Logout", token))).StatusCode);
        Assert.Null(session.SessionCookie);
        Assert.Equal(HttpStatusCode.OK, (await guesser.SendAsync(PostWithToken("/Account/Login", guessToken))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await guesser.SendAsync(PostWithToken("/Account/Login", guessToken))).StatusCode);
    }

    [Fact]
    public async Task Sign_out_still_requires_a_session_and_post()
    {
        var anonymous = vault.CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.PostAsync("/Account/Logout", null)).StatusCode);
        var session = await vault.SignInNewAccountAsync("it-sign-out-post", IPAddress.Parse("198.51.100.94"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await session.Client.GetAsync("/Account/Logout")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    private int PermitLimit => vault.Services.GetRequiredService<IOptions<AuthThrottleOptions>>().Value.PermitLimit;

    [Fact]
    public async Task The_legacy_passkey_calls_still_send_the_page_token_in_the_same_header()
    {
        var admin = await vault.AdminClientAsync();
        var token = VaultFixture.FormTokenOf(await admin.GetStringAsync("/Profile"), "/Profile");

        var refused = await admin.PostAsync("/Profile/PasskeyRegisterBegin", content: null);
        var accepted = await admin.SendAsync(PostWithToken("/Profile/PasskeyRegisterBegin", token));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("application/json", accepted.Content.Headers.ContentType?.MediaType);
        Assert.True((await accepted.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("challenge", out _));
    }

    private static SetCookieHeaderValue XsrfCookieOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookies = response.Headers.TryGetValues(HeaderNames.SetCookie, out var values) ? values.ToList() : [];
        return Assert.Single(SetCookieHeaderValue.ParseList(cookies), cookie => cookie.Name == XsrfCookie);
    }

    private static HttpRequestMessage PostWithToken(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(TokenHeader, token);
        return request;
    }
}
