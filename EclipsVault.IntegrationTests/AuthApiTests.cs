using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Security;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The sign-in UI API on the Production host (ADR-0016, ADR-0032): every call's answer and code, the
/// request token each change of identity reissues, the pending sign-in's rules, the throttle, and
/// what an anonymous caller can and cannot learn.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class AuthApiTests(VaultFixture vault)
{
    private const string Password = "It-auth-api-password-2026!";
    private const string PendingCookie = "EclipsVault.MfaPending";

    [Fact]
    public async Task Options_answer_anonymously_and_are_never_stored()
    {
        var response = await vault.CreateClient().GetAsync("/api/ui/auth/options");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var options = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sso = vault.Services.GetRequiredService<ISsoAvailability>();
        Assert.Equal(sso.Enabled, options.GetProperty("ssoEnabled").GetBoolean());
        Assert.Equal(sso.DisplayName, options.GetProperty("ssoDisplayName").GetString());
    }

    [Fact]
    public async Task Each_change_of_identity_reissues_the_token_and_the_next_post_validates_it()
    {
        const string username = "it-auth-token-chain";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.1"), password: Password);
        var secret = await AuthApi.TotpSecretAsync(vault, username);
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.2"));
        var anonymous = await AuthApi.AnonymousTokenAsync(browser);

        var pending = await AuthApi.PendingAsync(browser, username, Password);
        // The anonymous page's token no longer names who this browser is.
        await UiApiFailureContractTests.AssertProblemAsync(
            await AuthApi.PostAsync(browser, "/api/ui/auth/totp", anonymous, new { code = Totp.NextCode(secret) }), 400, "antiforgery_rejected");

        var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/totp", pending, new { code = Totp.NextCode(secret), returnUrl = "/Access" }));
        Assert.Equal("/Access", signedIn.GetProperty("redirect").GetString());
        Assert.NotNull(browser.SessionCookie);
        Assert.Null(browser.Cookies.GetCookies(browser.Client.BaseAddress!)[PendingCookie]);
        var session = AuthApi.ReissuedToken(browser);
        var copied = vault.CreateSession(browser.SessionCookie, IPAddress.Parse("100.74.174.3"));

        var signedOut = await AuthApi.PostAsync(browser, "/api/ui/auth/sign-out", session);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        Assert.Null(browser.SessionCookie);
        await UiApiFailureContractTests.AssertProblemAsync(await copied.Client.GetAsync("/api/ui/me"), 401, "unauthenticated");

        // The signed-out browser is anonymous again: the session's token no longer serves, the reissued one does.
        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/password", session,
            new { username, password = "It-not-the-password-2026!" }), 400, "antiforgery_rejected");
        var afterSignOut = AuthApi.ReissuedToken(browser);
        await AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/password", afterSignOut,
            new { username, password = "It-not-the-password-2026!" }), 400, "sign_in_failed", "Invalid username or password.");
    }

    [Theory]
    [InlineData("it-auth-land-default", null, "/Secrets", "100.74.174.4")]
    [InlineData("it-auth-land-outside", "https://outside.example/", "/Secrets", "100.74.174.40")]
    [InlineData("it-auth-land-account", "/Account/Totp", "/Secrets", "100.74.174.41")]
    [InlineData("it-auth-land-local", "/Access", "/Access", "100.74.174.42")]
    [InlineData("it-auth-land-app-relative", "~/Access", "/Access", "100.74.174.43")]
    public async Task A_finished_sign_in_lands_where_the_server_judges(string username, string? returnUrl, string expected, string source)
    {
        // A first sign-in spends three requests of its source's budget, so each case has its own source.
        await vault.SignInNewAccountAsync(username, IPAddress.Parse(source), password: Password);
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.5"));
        var token = await AuthApi.PendingAsync(browser, username, Password);

        var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/totp", token,
            new { code = Totp.NextCode(await AuthApi.TotpSecretAsync(vault, username)), returnUrl }));

        Assert.Equal(expected, signedIn.GetProperty("redirect").GetString());
    }

    [Fact]
    public async Task A_wrong_credential_is_a_400_with_the_pages_own_sentence_and_never_a_401()
    {
        const string username = "it-auth-wrong";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.6"), password: Password);
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.7"));

        await AssertProblemAsync(await AuthApi.PasswordAsync(browser, username, "It-wrong-password-2026!"),
            400, "sign_in_failed", "Invalid username or password.");
        var token = await AuthApi.PendingAsync(browser, username, Password);
        await AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/totp", token, new { code = "000000" }),
            400, "code_invalid", "Invalid authenticator code.");
        await AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/recovery-code", token, new { code = "AAAAA-BBBBB" }),
            400, "code_invalid", "That recovery code is not valid, or it has already been used.");
        var malformed = await ProfileApi.AssertProblemAsync(
            await AuthApi.PostAsync(browser, "/api/ui/auth/totp", token, new { code = "12" }), 400, "validation_failed");
        Assert.Equal(["code"], ProfileApi.NamesOf(malformed.GetProperty("errors")));
        await AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/recover", await AuthApi.AnonymousTokenAsync(browser, "/Account/Recover"),
            new { username, password = Password, code = "000000" }), 400, "recovery_failed", "Recovery could not be completed with these credentials.");
    }

    [Fact]
    public async Task A_recovery_code_sign_in_says_how_many_codes_are_left()
    {
        const string username = "it-auth-recovery";
        var owner = await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.8"), password: Password);
        var codes = await ProfileApi.RecoverySetAsync(owner.ProfileOwner!);
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.9"));
        var token = await AuthApi.PendingAsync(browser, username, Password);

        var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/recovery-code", token, new { code = codes[0] }));

        Assert.Equal("/Secrets", signedIn.GetProperty("redirect").GetString());
        Assert.Equal(9, signedIn.GetProperty("codesRemaining").GetInt32());
        Assert.NotNull(browser.SessionCookie);
    }

    [Fact]
    public async Task First_enrollment_shows_the_authenticator_as_text_and_png_and_confirms_into_a_session()
    {
        const string username = "it-auth-enroll";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.10"), password: Password);
        await WithoutAuthenticatorAsync(username);
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.11"));
        var token = await AuthApi.PendingAsync(browser, username, Password, next: "enroll");

        var enrollment = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/enrollment", token));
        Assert.StartsWith("data:image/png;base64,", enrollment.GetProperty("qrCodeDataUri").GetString(), StringComparison.Ordinal);
        var secret = enrollment.GetProperty("secretBase32").GetString()!;
        await AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/enrollment/confirm", token, new { code = "000000" }),
            400, "code_invalid", "The confirmation code did not match. Try again.");
        var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/enrollment/confirm", token,
            new { code = Totp.Code(secret, Totp.CurrentStep()) }));

        Assert.Equal("/Secrets", signedIn.GetProperty("redirect").GetString());
        Assert.True((await ProfileHttp.AccountAsync(vault, username)).TotpEnabled);
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task A_discoverable_passkey_signs_in_without_a_username()
    {
        const string username = "it-auth-passkey";
        var owner = await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.12"), password: Password);
        using var authenticator = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner, authenticator, "Discoverable");
        var account = await ProfileHttp.AccountAsync(vault, username);
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.13"));
        var token = await AuthApi.AnonymousTokenAsync(browser);
        var before = await AuditTrail.HeadAsync(vault);

        var options = (await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/begin", token, new { returnUrl = "/Access" })))
            .GetProperty("publicKey");
        Assert.Empty(options.GetProperty("allowCredentials").EnumerateArray());
        var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/complete", token,
            new { credential = authenticator.Assert(options) }));

        Assert.Equal("/Access", signedIn.GetProperty("redirect").GetString());
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal([AuditAction.SignInFactorVerified, AuditAction.PasskeyLogin], await ProfileHttp.EventsAfterAsync(vault, before, account.Id));
    }

    [Fact]
    public async Task A_passkey_that_does_not_verify_or_has_no_ceremony_gets_no_session()
    {
        const string username = "it-auth-passkey-refused";
        var owner = await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.14"), password: Password);
        using var authenticator = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner, authenticator, "Refused");
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.15"));
        var token = await AuthApi.AnonymousTokenAsync(browser);
        var options = (await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/begin", token, new { }))).GetProperty("publicKey");

        await AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/complete", token,
            new { credential = authenticator.AssertWithWrongSignature(options) }), 400, "passkey_failed", "This passkey could not be verified.");
        // The ceremony was spent by that attempt.
        await AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/complete", token,
            new { credential = authenticator.Assert(options) }), 409, "sign_in_restarted", "Sign-in timed out. Try again.");
        Assert.Null(browser.SessionCookie);
    }

    [Theory]
    [InlineData("/api/ui/auth/totp")]
    [InlineData("/api/ui/auth/recovery-code")]
    [InlineData("/api/ui/auth/enrollment")]
    [InlineData("/api/ui/auth/enrollment/confirm")]
    public async Task A_second_factor_call_without_a_pending_sign_in_is_sign_in_expired(string path)
    {
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.16"));
        var token = await AuthApi.AnonymousTokenAsync(browser);

        await AssertProblemAsync(await AuthApi.PostAsync(browser, path, token, new { code = "123456" }), 401, "sign_in_expired", "Your sign-in expired. Sign in again.");
    }

    [Fact]
    public async Task Sign_out_without_a_session_is_unauthenticated()
    {
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.16"));

        await UiApiFailureContractTests.AssertProblemAsync(
            await AuthApi.PostAsync(browser, "/api/ui/auth/sign-out", await AuthApi.AnonymousTokenAsync(browser)), 401, "unauthenticated");
    }

    [Theory]
    [InlineData("it-auth-session-totp", "/api/ui/auth/totp", "100.74.174.35")]
    [InlineData("it-auth-session-recovery", "/api/ui/auth/recovery-code", "100.74.174.36")]
    [InlineData("it-auth-session-enroll", "/api/ui/auth/enrollment", "100.74.174.37")]
    [InlineData("it-auth-session-confirm", "/api/ui/auth/enrollment/confirm", "100.74.174.38")]
    public async Task A_session_alone_is_not_a_pending_sign_in(string username, string path, string source)
    {
        var owner = await vault.SignInNewAccountAsync(username, IPAddress.Parse(source), password: Password);

        await UiApiFailureContractTests.AssertProblemAsync(
            await AuthApi.PostAsync(owner, path, await UiApiRequest.AntiforgeryTokenAsync(owner.Client), new { code = "123456" }), 401, "sign_in_expired");
    }

    [Fact]
    public async Task A_session_still_reaches_the_password_stage_and_signs_out()
    {
        var owner = await vault.SignInNewAccountAsync("it-auth-session-only", IPAddress.Parse("100.74.174.17"), password: Password);
        var token = await UiApiRequest.AntiforgeryTokenAsync(owner.Client);

        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(owner, "/api/ui/auth/password", token,
            new { username = "it-auth-session-only", password = "It-wrong-password-2026!" }), 400, "sign_in_failed");
        Assert.Equal(HttpStatusCode.NoContent, (await AuthApi.PostAsync(owner, "/api/ui/auth/sign-out", token)).StatusCode);
    }

    [Fact]
    public async Task A_pending_sign_in_without_a_session_proceeds_starts_over_and_cannot_sign_out()
    {
        const string username = "it-auth-pending-only";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.18"), password: Password);
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.19"));
        var pending = await AuthApi.PendingAsync(browser, username, Password);

        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/sign-out", pending), 401, "unauthenticated");
        // Back on Login, the anonymous page's token starts the password stage again.
        var restarted = await AuthApi.PendingAsync(browser, username, Password);
        Assert.Equal(HttpStatusCode.OK, (await AuthApi.PostAsync(browser, "/api/ui/auth/totp", restarted,
            new { code = Totp.NextCode(await AuthApi.TotpSecretAsync(vault, username)) })).StatusCode);
    }

    [Fact]
    public async Task A_stale_pending_sign_in_is_expired_and_enrollment_does_not_run()
    {
        const string username = "it-auth-stale-pending";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.20"), password: Password);
        await WithoutAuthenticatorAsync(username);
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.21"));
        var token = await AuthApi.PendingAsync(browser, username, Password, next: "enroll");
        var account = await ProfileHttp.AccountAsync(vault, username);
        await ProfileHttp.AdvanceCredentialGenerationAsync(vault, account.Id);

        await AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/enrollment", token), 401, "sign_in_expired", "Your sign-in expired. Sign in again.");

        Assert.Equal(account.TotpSecret, (await ProfileHttp.AccountAsync(vault, username)).TotpSecret);
        Assert.Null(browser.Cookies.GetCookies(browser.Client.BaseAddress!)[PendingCookie]);
    }

    [Fact]
    public async Task An_anonymous_call_drops_a_stale_pending_sign_in_before_it_runs()
    {
        const string username = "it-auth-stale-anonymous";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.22"), password: Password);
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.23"));
        var stale = await AuthApi.PendingAsync(browser, username, Password);
        await ProfileHttp.AdvanceCredentialGenerationAsync(vault, (await ProfileHttp.AccountAsync(vault, username)).Id);

        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/auth/options")).StatusCode);

        Assert.Null(browser.Cookies.GetCookies(browser.Client.BaseAddress!)[PendingCookie]);
        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/sign-out", stale), 401, "unauthenticated");
    }

    [Fact]
    public async Task An_expired_pending_cookie_is_treated_as_missing_and_dropped()
    {
        const string username = "it-auth-expired-pending";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.24"), password: Password);
        var original = vault.CreateSession(source: IPAddress.Parse("100.74.174.25"));
        var token = await AuthApi.PendingAsync(original, username, Password);
        var browser = vault.CreateSession(source: IPAddress.Parse("100.74.174.25"));
        browser.Cookies.Add(browser.Client.BaseAddress!, Expired(original.Cookies.GetCookies(original.Client.BaseAddress!)[PendingCookie]!));
        browser.Cookies.Add(browser.Client.BaseAddress!, original.Cookies.GetCookies(original.Client.BaseAddress!)["EclipsVault.Csrf"]!);

        await AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/totp", token, new { code = "123456" }), 401, "sign_in_expired", "Your sign-in expired. Sign in again.");
        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/sign-out", token), 401, "unauthenticated");
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/auth/options")).StatusCode);

        Assert.Null(browser.Cookies.GetCookies(browser.Client.BaseAddress!)[PendingCookie]);
    }

    [Fact]
    public async Task The_throttle_meters_each_sign_in_post_but_not_sign_out_or_reads()
    {
        var source = IPAddress.Parse("100.74.174.26");
        var original = await vault.SignInNewAccountAsync("it-auth-throttled", IPAddress.Parse("100.74.174.27"), password: Password);
        var session = vault.CreateSession(original.SessionCookie, source);
        var pending = vault.CreateSession(source: source);
        var pendingToken = await AuthApi.PendingAsync(pending, "it-auth-throttled", Password);
        var guesser = vault.CreateSession(source: source);
        var token = await AuthApi.AnonymousTokenAsync(guesser);

        // The password stage above spent the first attempt.
        Assert.All(await SpendCredentialAttemptsAsync(guesser, token, PermitLimit - 1), status => Assert.Equal(HttpStatusCode.BadRequest, status));

        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(pending, "/api/ui/auth/totp", pendingToken, Guess), 429, "throttled");
        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(pending, "/api/ui/auth/recovery-code", pendingToken, new { code = "AAAAA-BBBBB" }), 429, "throttled");
        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(pending, "/api/ui/auth/enrollment", pendingToken), 429, "throttled");
        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(pending, "/api/ui/auth/enrollment/confirm", pendingToken, Guess), 429, "throttled");

        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(guesser, "/api/ui/auth/password", token, Guess), 429, "throttled");
        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(guesser, "/api/ui/auth/passkey/begin", token, Guess), 429, "throttled");
        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(guesser, "/api/ui/auth/passkey/complete", token, Guess), 429, "throttled");
        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(guesser, "/api/ui/auth/recover", token, Guess), 429, "throttled");

        Assert.Equal(HttpStatusCode.OK, (await guesser.Client.GetAsync("/api/ui/auth/options")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guesser.Client.GetAsync("/Account/Login")).StatusCode);
        var sessionToken = await UiApiRequest.AntiforgeryTokenAsync(session.Client);
        Assert.Equal(HttpStatusCode.NoContent, (await AuthApi.PostAsync(session, "/api/ui/auth/sign-out", sessionToken)).StatusCode);
    }

    [Fact]
    public async Task Unknown_locked_and_disabled_usernames_get_the_same_answer()
    {
        await vault.SignInNewAccountAsync("it-auth-locked", IPAddress.Parse("100.74.174.28"), password: Password);
        await vault.SignInNewAccountAsync("it-auth-disabled", IPAddress.Parse("100.74.174.28"), password: Password);
        using (var setup = vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            await db.Users.Where(user => user.Username == "it-auth-locked")
                .ExecuteUpdateAsync(set => set.SetProperty(user => user.LockedUntilUtc, DateTimeOffset.UtcNow.AddHours(1)));
            await db.Users.Where(user => user.Username == "it-auth-disabled")
                .ExecuteUpdateAsync(set => set.SetProperty(user => user.IsDisabled, true));
        }

        // The right password, where there is one: still nothing tells the caller the account exists.
        var unknown = await PasswordAnswerAsync("it-auth-no-such-account");

        Assert.Equal(unknown, await PasswordAnswerAsync("it-auth-locked"));
        Assert.Equal(unknown, await PasswordAnswerAsync("it-auth-disabled"));
        Assert.Equal("sign_in_failed", JsonDocument.Parse(unknown).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_blocked_network_reaches_only_the_recover_page_and_the_recover_call()
    {
        var source = IPAddress.Parse("100.74.174.30");
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync(source.ToString(), "auth surface probe");
        try
        {
            var browser = vault.CreateSession(source: source);
            var token = await AuthApi.AnonymousTokenAsync(browser, "/Account/Recover");

            await AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/recover", token,
                new { username = "it-auth-no-such-account", password = Password, code = "123456" }), 400, "recovery_failed",
                "Recovery could not be completed with these credentials.");
            Assert.Equal(HttpStatusCode.OK, (await browser.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/account/recover"))).StatusCode);
            // The document's own static files load too, so the page works from here.
            var assets = Regex.Matches(await browser.Client.GetStringAsync("/Account/Recover"), "(?:src|href)=\"(/[^\"]+)\"")
                .Select(match => match.Groups[1].Value).ToList();
            Assert.Contains(assets, asset => asset.StartsWith("/ui/main-", StringComparison.Ordinal));
            foreach (var asset in assets)
            {
                Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync(asset)).StatusCode);
            }

            // The Legacy form's POST and any other path or method under the page are blocked like the rest.
            foreach (var (method, path) in new[] { ("POST", "/Account/Recover"), ("GET", "/Account/Recover/x"), ("GET", "/Account/Recover/") })
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path)
                {
                    Content = method == "POST" ? new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }) : null,
                };
                using var response = await browser.Client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Contains("<h1>Access blocked</h1>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/password", token, Guess), 403, "blocked");
            await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/begin", token, Guess), 403, "blocked");
            await UiApiFailureContractTests.AssertProblemAsync(await browser.Client.GetAsync("/api/ui/auth/options"), 403, "blocked");
            await UiApiFailureContractTests.AssertProblemAsync(await browser.Client.GetAsync("/api/ui/auth/recover"), 403, "blocked");
            await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/recover/", token, Guess), 403, "blocked");
            Assert.Equal(HttpStatusCode.Forbidden, (await browser.Client.GetAsync("/Account/Login")).StatusCode);
        }
        finally
        {
            await (await blacklist.LiftForRecoveryAsync(source)).ReleaseAsync();
        }
    }

    [Fact]
    public async Task Recovery_from_the_recover_page_lifts_the_block_and_records_its_rows_in_order()
    {
        const string username = "it-auth-page-recover";
        var source = IPAddress.Parse("100.74.174.31");
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.32"), ClearanceLevel.TopSecret, password: Password);
        var account = await ProfileHttp.AccountAsync(vault, username);
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync(source.ToString(), "page recover");
        var browser = vault.CreateSession(source: source);
        var token = await AuthApi.AnonymousTokenAsync(browser, "/Account/Recover");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AuthApi.PostAsync(browser, "/api/ui/auth/recover", token,
            new { username, password = Password, code = Totp.NextCode(account.TotpSecret!) });

        Assert.True((await AuthApi.JsonAsync(response)).GetProperty("blockLifted").GetBoolean());
        Assert.False(await blacklist.IsBlockedAsync(source));
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
        var rows = (await AuditTrail.RowsAfterAsync(vault, before)).Where(row => row.UserId == account.Id).ToList();
        Assert.Equal([AuditAction.SignInFactorVerified, AuditAction.LoginSucceeded, AuditAction.BreakGlassRecovery, AuditAction.IpRangeUnblocked],
            rows.Select(row => row.Action));
        Assert.Equal("Intrusion-defence block lifted by break-glass recovery: 100.74.174.31/32", rows[3].Details);
    }

    [Fact]
    public async Task My_activity_names_the_ranges_a_break_glass_recovery_lifted()
    {
        const string username = "it-auth-page-recover-activity";
        var source = IPAddress.Parse("100.74.174.60");
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.61"), ClearanceLevel.TopSecret, password: Password);
        var account = await ProfileHttp.AccountAsync(vault, username);
        await vault.Services.GetRequiredService<IIpBlacklist>().BlockAsync(source.ToString(), "page recover activity");
        var browser = vault.CreateSession(source: source);
        var token = await AuthApi.AnonymousTokenAsync(browser, "/Account/Recover");
        await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/recover", token,
            new { username, password = Password, code = Totp.NextCode(account.TotpSecret!) }));

        var myActivity = await browser.Client.GetFromJsonAsync<ActivityResponse>("/api/ui/activity");

        Assert.Equal("100.74.174.60/32", Assert.Single(myActivity!.Items, item => item.Title == "Unblocked an IP range").ResourceName);
    }

    [Fact]
    public async Task Recovery_shows_all_lifted_ranges_in_my_activity_when_their_names_exceed_the_audit_column()
    {
        const string username = "it-auth-page-recover-many-ranges";
        const string ranges = "2001:db8:205::7%1/128, 2001:db8:205::7%2/128, 2001:db8:205::7%3/128, 2001:db8:205::7%4/128, "
            + "2001:db8:205::7%5/128, 2001:db8:205::7%6/128, 2001:db8:205::7%7/128, 2001:db8:205::7%8/128";
        var source = IPAddress.Parse("2001:db8:205::7");
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("100.74.174.62"), ClearanceLevel.TopSecret, password: Password);
        var account = await ProfileHttp.AccountAsync(vault, username);
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        // A host CIDR retains its IPv6 scope id, but containment tests the address bits.
        foreach (var scope in Enumerable.Range(1, 8))
        {
            await blacklist.BlockAsync($"2001:db8:205::7%{scope}", "overlapping host block");
        }

        var browser = vault.CreateSession(source: source);
        var token = await AuthApi.AnonymousTokenAsync(browser, "/Account/Recover");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AuthApi.PostAsync(browser, "/api/ui/auth/recover", token,
            new { username, password = Password, code = Totp.NextCode(account.TotpSecret!) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await AuthApi.JsonAsync(response)).GetProperty("blockLifted").GetBoolean());
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before),
            entry => entry.UserId == account.Id && entry.Action == AuditAction.IpRangeUnblocked);
        Assert.InRange(row.ResourceName!.Length, 1, 128);
        Assert.Equal($"Intrusion-defence block lifted by break-glass recovery: {ranges}", row.Details);
        var myActivity = await browser.Client.GetFromJsonAsync<ActivityResponse>("/api/ui/activity");
        Assert.Equal(ranges, Assert.Single(myActivity!.Items, item => item.Title == "Unblocked an IP range").ResourceName);
    }

    [Fact]
    public async Task Local_sign_out_from_a_blocked_network_clears_only_this_browsers_cookie()
    {
        var source = IPAddress.Parse("100.74.174.33");
        var owner = await vault.SignInNewAccountAsync("it-auth-blocked-local", IPAddress.Parse("100.74.174.34"));
        var blocked = vault.CreateSession(owner.SessionCookie, source);
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync(source.ToString(), "local sign-out probe");
        try
        {
            var token = await blocked.Client.GetFromJsonAsync<LocalSignOut.TokenResponse>(LocalSignOut.Path);
            using var clear = new HttpRequestMessage(HttpMethod.Post, LocalSignOut.Path);
            clear.Headers.Add("RequestVerificationToken", token!.RequestToken);

            var response = await blocked.Client.SendAsync(clear);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Null(blocked.SessionCookie);
            // Nothing was revoked, and the blocked address gained nothing.
            Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
            await UiApiFailureContractTests.AssertProblemAsync(
                await vault.CreateSession(owner.SessionCookie, source).Client.GetAsync("/api/ui/me"), 403, "blocked");
        }
        finally
        {
            await (await blacklist.LiftForRecoveryAsync(source)).ReleaseAsync();
        }
    }

    /// <summary>A body every sign-in call binds, naming no account.</summary>
    private static readonly object Guess = new { username = "it-auth-no-such-account", password = "It-guess-2026!", code = "123456", credential = new { } };

    private int PermitLimit => vault.Services.GetRequiredService<IOptions<AuthThrottleOptions>>().Value.PermitLimit;

    private static async Task<HttpStatusCode[]> SpendCredentialAttemptsAsync(VaultSession guesser, string token, int count)
    {
        var statuses = new HttpStatusCode[count];
        for (var attempt = 0; attempt < count; attempt++)
        {
            statuses[attempt] = (await AuthApi.PostAsync(guesser, "/api/ui/auth/password", token, Guess)).StatusCode;
        }

        return statuses;
    }

    private async Task<string> PasswordAnswerAsync(string username)
    {
        var response = await AuthApi.PasswordAsync(vault.CreateSession(source: IPAddress.Parse("100.74.174.29")), username, Password);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, int status, string code, string title)
        => Assert.Equal(title, (await ProfileApi.AssertProblemAsync(response, status, code)).GetProperty("title").GetString());

    private async Task WithoutAuthenticatorAsync(string username)
    {
        using var setup = vault.Services.CreateScope();
        var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.Users.Where(user => user.Username == username).ExecuteUpdateAsync(set => set
            .SetProperty(user => user.TotpEnabled, false)
            .SetProperty(user => user.LastTotpStep, (long?)null));
    }

    private Cookie Expired(Cookie cookie)
    {
        var format = vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AuthSchemes.MfaPending).TicketDataFormat;
        var ticket = format.Unprotect(cookie.Value)!;
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        return new Cookie(cookie.Name, format.Protect(ticket), "/") { Secure = true, HttpOnly = true };
    }
}
