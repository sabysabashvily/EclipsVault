using System.Globalization;
using System.Net;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The sign-in pages the Angular UI serves at their Legacy URLs (ADR-0032): who gets each document,
/// what it carries, and that the retired Legacy actions behind them no longer act.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SignInPagesTests(VaultFixture vault)
{
    private const string Password = "It-sign-in-pages-password-2026!";
    private const string PendingCookie = "EclipsVault.MfaPending";
    private static int _nextSource;

    private const string AngularPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; frame-src 'none'; " +
        "frame-ancestors 'none'; form-action 'self'; base-uri 'self'" +
        "; require-trusted-types-for 'script'; trusted-types angular angular#bundler";

    // Login's single sign-on form redirects to the identity provider, so its origin, and only it, joins form-action.
    private const string LoginPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; frame-src 'none'; " +
        "frame-ancestors 'none'; form-action 'self' https://idp.example.test; base-uri 'self'" +
        "; require-trusted-types-for 'script'; trusted-types angular angular#bundler";

    [Theory]
    [InlineData("GET", "/Account/Login", LoginPolicy)]
    [InlineData("HEAD", "/Account/Login", LoginPolicy)]
    [InlineData("GET", "/account/login/", LoginPolicy)]
    [InlineData("GET", "/Account/Login?ReturnUrl=%2FAccess", LoginPolicy)]
    [InlineData("GET", "/Account/Recover", AngularPolicy)]
    [InlineData("HEAD", "/Account/Recover", AngularPolicy)]
    [InlineData("GET", "/account/recover/", AngularPolicy)]
    public async Task An_anonymous_visitor_gets_an_anonymous_sign_in_document_with_a_request_token(string method, string path, string policy)
    {
        var browser = vault.CreateSession(source: NextSource());

        using var response = await browser.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        AssertSignInDocument(response, policy);
    }

    [Theory]
    [InlineData("/Account/Login", "/Secrets")]
    [InlineData("/Account/Login?ReturnUrl=%2FAccess%3Fview%3Dmine", "/Access?view=mine")]
    [InlineData("/Account/Login?ReturnUrl=https%3A%2F%2Foutside.example%2F", "/Secrets")]
    [InlineData("/Account/Login?ReturnUrl=%2F%2Foutside.example%2F", "/Secrets")]
    public async Task A_signed_in_visit_to_Login_goes_on_to_its_judged_target(string path, string target)
    {
        var client = await vault.AdminClientAsync();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(target, response.Headers.Location?.OriginalString);
        Assert.DoesNotContain(response.Headers.TryGetValues(HeaderNames.SetCookie, out var cookies) ? cookies : [],
            cookie => cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_signed_in_visitor_gets_Recover_with_a_token_its_call_accepts()
    {
        var username = "it-pages-recover-" + Guid.NewGuid().ToString("N")[..8];
        var signedIn = await vault.SignInNewAccountAsync(username, NextSource(), password: Password);

        await AssertRecoverAcceptsItsTokenAsync(signedIn, username);
    }

    [Fact]
    public async Task A_pending_sign_in_gets_Recover_with_a_token_its_call_accepts()
    {
        var username = "it-pages-recover-" + Guid.NewGuid().ToString("N")[..8];
        await vault.SignInNewAccountAsync(username, NextSource(), password: Password);
        var pending = vault.CreateSession(source: NextSource());
        await AuthApi.PendingAsync(pending, username, Password);

        await AssertRecoverAcceptsItsTokenAsync(pending, username);
    }

    /// <summary>The Recover document's token passes antiforgery on the recover call for whoever loaded it.</summary>
    private static async Task AssertRecoverAcceptsItsTokenAsync(VaultSession browser, string username)
    {
        using var response = await browser.Client.GetAsync("/Account/Recover");

        AssertSignInDocument(response);
        // A malformed code reaches validation only once antiforgery has accepted the token.
        await ProfileApi.AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/recover", SignInDocument.TokenOf(response),
            new { username, password = Password, code = "12" }), 400, "validation_failed");
    }

    [Theory]
    [InlineData("/Account/Totp")]
    [InlineData("/Account/RecoveryCode")]
    [InlineData("/Account/EnrollTotp")]
    public async Task A_second_factor_page_without_a_pending_sign_in_sends_the_visitor_to_Login(string path)
    {
        var anonymous = vault.CreateSession(source: NextSource());
        var signedIn = await vault.AdminClientAsync();

        using var fromAnonymous = await anonymous.Client.GetAsync(path);
        using var fromSession = await signedIn.GetAsync(path);

        Assert.All(new[] { fromAnonymous, fromSession }, response =>
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
            Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        });
    }

    [Theory]
    [InlineData("/Account/Totp", "totp")]
    [InlineData("/Account/RecoveryCode", "totp")]
    [InlineData("/Account/Totp?ReturnUrl=%2FAccess", "totp")]
    [InlineData("/account/totp/", "totp")]
    [InlineData("/Account/EnrollTotp", "enroll")]
    public async Task A_pending_sign_in_gets_its_second_factor_page_with_a_token_for_that_sign_in(string path, string next)
    {
        var username = "it-pages-" + next + "-" + Guid.NewGuid().ToString("N")[..8];
        var owner = await vault.SignInNewAccountAsync(username, NextSource(), password: Password);
        if (next == "enroll")
        {
            await ProfileHttp.ResetAuthenticatorAsync(owner);
        }
        var browser = vault.CreateSession(source: NextSource());
        await AuthApi.PendingAsync(browser, username, Password, next);

        using var response = await browser.Client.GetAsync(path);

        AssertSignInDocument(response);
        // The token names the pending sign-in, so the page's own calls are accepted.
        var token = SignInDocument.TokenOf(response);
        // A malformed code reaches validation only once antiforgery has accepted the token.
        await ProfileApi.AssertProblemAsync(
            await AuthApi.PostAsync(browser, "/api/ui/auth/totp", token, new { code = "12" }), 400, "validation_failed");
    }

    /// <summary>
    /// The Legacy form actions behind the sign-in pages are gone. Their paths answer as every other
    /// retired route does: a signed-in caller gets 405 or 404, and anyone else, a pending sign-in too,
    /// gets the default-deny redirect to sign in. A complete, valid submission changes nothing.
    /// </summary>
    [Theory]
    [InlineData("/Account/Login", HttpStatusCode.MethodNotAllowed)]
    [InlineData("/account/login/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("/Account/Totp", HttpStatusCode.MethodNotAllowed)]
    [InlineData("/Account/RecoveryCode", HttpStatusCode.MethodNotAllowed)]
    [InlineData("/Account/EnrollTotp", HttpStatusCode.MethodNotAllowed)]
    [InlineData("/Account/Recover", HttpStatusCode.MethodNotAllowed)]
    [InlineData("/Account/PasskeyLoginBegin", HttpStatusCode.NotFound)]
    [InlineData("/Account/PasskeyLoginComplete", HttpStatusCode.NotFound)]
    public async Task A_retired_sign_in_action_neither_signs_in_nor_records_anything(string path, HttpStatusCode expected)
    {
        var username = "it-pages-retired-" + Guid.NewGuid().ToString("N")[..8];
        var signedIn = await vault.SignInNewAccountAsync(username, NextSource(), password: Password);
        var signedInToken = await UiApiRequest.AntiforgeryTokenAsync(signedIn.Client);
        var account = await ProfileHttp.AccountAsync(vault, username);
        var anonymous = vault.CreateSession(source: NextSource());
        var pending = vault.CreateSession(source: NextSource());
        var pendingToken = await AuthApi.PendingAsync(pending, username, Password);
        var anonymousToken = await AuthApi.AnonymousTokenAsync(anonymous);
        var head = await AuditTrail.HeadAsync(vault);

        using var fromAnonymous = await PostFormAsync(anonymous.Client, path, anonymousToken, username, account.TotpSecret!);
        using var fromPending = await PostFormAsync(pending.Client, path, pendingToken, username, account.TotpSecret!);
        using var fromSignedIn = await PostFormAsync(signedIn.Client, path, signedInToken, username, account.TotpSecret!);

        Assert.Equal(expected, fromSignedIn.StatusCode);
        foreach (var response in new[] { fromAnonymous, fromPending })
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        }
        Assert.Null(anonymous.SessionCookie);
        Assert.Null(anonymous.Cookies.GetCookies(anonymous.Client.BaseAddress!)[PendingCookie]);
        Assert.Null(pending.SessionCookie);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, head));
        var after = await ProfileHttp.AccountAsync(vault, username);
        Assert.Equal(account.LastTotpStep, after.LastTotpStep);
        Assert.Equal(account.FailedAccessCount, after.FailedAccessCount);
    }

    private static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string token, string username, string secret)
        => UiApiRequest.SendWithTokenAsync(client, token, HttpMethod.Post, path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = username,
            ["Password"] = Password,
            ["Code"] = Totp.NextCode(secret),
            ["credential"] = "{}",
        }));

    // Each client its own address: the vault allows each address only a few sign-in steps per window.
    private static IPAddress NextSource()
        => IPAddress.Parse("2001:db8:175::" + Interlocked.Increment(ref _nextSource).ToString("x", CultureInfo.InvariantCulture));

    private static void AssertSignInDocument(HttpResponseMessage response, string policy = AngularPolicy)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(policy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.True(response.Headers.CacheControl?.NoStore);
        var token = Assert.Single(SetCookieHeaderValue.ParseList(response.Headers.GetValues(HeaderNames.SetCookie).ToList()),
            cookie => cookie.Name == "XSRF-TOKEN");
        Assert.True(token.Secure);
        Assert.False(token.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, token.SameSite);
    }
}
