using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The return URL a sign-in page carries in its address. The pages pass it with each call, and the
/// server judges it only when the last factor succeeds (ADR-0032).
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SignInReturnUrlTests(VaultFixture vault)
{
    private static int _nextAddress;

    [Fact]
    public async Task Enrollment_completion_honors_a_local_target()
    {
        var (username, password) = await CreateAccountAsync();
        var browser = NewSession();
        var token = await AuthApi.PendingAsync(browser, username, password, next: "enroll");
        var secret = await EnrollmentSecretAsync(browser, token);

        var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/enrollment/confirm", token,
            new { code = Totp.Code(secret, Totp.CurrentStep()), returnUrl = "/Access" }));

        Assert.Equal("/Access", signedIn.GetProperty("redirect").GetString());
    }

    [Theory]
    [InlineData("/Access?view=mine&sort=name#matrix", "/Access?view=mine&sort=name#matrix")]
    [InlineData("https://outside.example/", "/Secrets")]
    public async Task Password_and_initial_enrollment_return_only_after_the_second_factor(string returnUrl, string expected)
    {
        var (username, password) = await CreateAccountAsync();
        var browser = NewSession();
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync(WithReturnUrl("/Account/Login", returnUrl))).StatusCode);

        var afterPassword = await AuthApi.JsonAsync(await AuthApi.PasswordAsync(browser, username, password, returnUrl));
        Assert.Equal("enroll", afterPassword.GetProperty("next").GetString());
        var token = AuthApi.ReissuedToken(browser);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync(WithReturnUrl("/Account/EnrollTotp", returnUrl))).StatusCode);
        var secret = await EnrollmentSecretAsync(browser, token);
        await ProfileApi.AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/enrollment/confirm", token,
            new { code = "bad", returnUrl }), 400, "validation_failed");
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/enrollment/confirm", token,
            new { code = Totp.Code(secret, Totp.CurrentStep() - 10), returnUrl }), 400, "code_invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);

        var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/enrollment/confirm", token,
            new { code = Totp.Code(secret, Totp.CurrentStep()), returnUrl }));

        Assert.Equal(expected, signedIn.GetProperty("redirect").GetString());
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Theory]
    [InlineData("/Access?view=mine&sort=name#matrix", "/Access?view=mine&sort=name#matrix")]
    [InlineData("https://outside.example/", "/Secrets")]
    public async Task Password_totp_and_alternate_methods_keep_the_target_across_errors(string returnUrl, string expected)
    {
        var (username, password, secret, enrolled, step) = await EnrollAccountAsync();
        enrolled.Dispose();
        var browser = NewSession();
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync(WithReturnUrl("/Account/Login", returnUrl))).StatusCode);
        await ProfileApi.AssertProblemAsync(await AuthApi.PasswordAsync(browser, username, "", returnUrl), 400, "validation_failed");
        await UiApiFailureContractTests.AssertProblemAsync(
            await AuthApi.PasswordAsync(browser, username, "incorrect-password", returnUrl), 400, "sign_in_failed");

        var afterPassword = await AuthApi.JsonAsync(await AuthApi.PasswordAsync(browser, username, password, returnUrl));
        Assert.Equal("totp", afterPassword.GetProperty("next").GetString());
        var token = AuthApi.ReissuedToken(browser);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync(WithReturnUrl("/Account/Totp", returnUrl))).StatusCode);
        await ProfileApi.AssertProblemAsync(
            await AuthApi.PostAsync(browser, "/api/ui/auth/totp", token, new { code = "bad", returnUrl }), 400, "validation_failed");
        await UiApiFailureContractTests.AssertProblemAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/totp", token,
            new { code = Totp.Code(secret, Totp.CurrentStep() - 10), returnUrl }), 400, "code_invalid");

        // The alternate method and the way back are pages of the same pending sign-in.
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync(WithReturnUrl("/Account/RecoveryCode", returnUrl))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync(WithReturnUrl("/Account/Totp", returnUrl))).StatusCode);
        var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/totp", token,
            new { code = Totp.Code(secret, Totp.StepStillAccepted(ServerNow(), step)), returnUrl }));

        Assert.Equal(expected, signedIn.GetProperty("redirect").GetString());
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Theory]
    [InlineData("/Access", "/Access")]
    [InlineData("https://outside.example/", "/Secrets")]
    public async Task Recovery_code_completion_checks_the_target_and_creates_a_real_session(string returnUrl, string expected)
    {
        var (username, password, secret, enrolled, _) = await EnrollAccountAsync();
        using (enrolled)
        {
            var operationId = await VerifyProfileChangeAsync(enrolled, password, secret, new { kind = "ReplaceRecoveryCodes" });
            var generated = await ProfileRequestAsync(enrolled, "/api/ui/profile/recovery-codes", new { operationId });
            var code = generated.GetProperty("codes")[0].GetString()!;
            Assert.NotEmpty(code);

            var browser = NewSession();
            Assert.Equal("totp", (await AuthApi.JsonAsync(await AuthApi.PasswordAsync(browser, username, password, returnUrl))).GetProperty("next").GetString());
            var token = AuthApi.ReissuedToken(browser);
            Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync(WithReturnUrl("/Account/RecoveryCode", returnUrl))).StatusCode);
            await ProfileApi.AssertProblemAsync(
                await AuthApi.PostAsync(browser, "/api/ui/auth/recovery-code", token, new { code = "bad", returnUrl }), 400, "validation_failed");
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
            await UiApiFailureContractTests.AssertProblemAsync(
                await AuthApi.PostAsync(browser, "/api/ui/auth/recovery-code", token, new { code = "ZZZZZ-ZZZZZ", returnUrl }), 400, "code_invalid");
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);

            var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/recovery-code", token, new { code, returnUrl }));

            Assert.Equal(expected, signedIn.GetProperty("redirect").GetString());
            Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
        }
    }

    [Fact]
    public async Task A_second_factor_page_that_expired_mid_sign_in_does_not_become_a_redirect_loop()
    {
        var (username, password, secret, enrolled, step) = await EnrollAccountAsync();
        enrolled.Dispose();
        var browser = NewSession();
        // Past the MFA-pending cookie's six minutes, the second-factor page challenges like any
        // other page and names itself as the place to come back to.
        var challenge = await browser.Client.GetAsync("/Account/Totp");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.Equal("/Account/Login", challenge.Headers.Location?.AbsolutePath);
        var returnUrl = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["ReturnUrl"].ToString();
        Assert.Equal("/Account/Totp", returnUrl);
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync(challenge.Headers.Location)).StatusCode);

        Assert.Equal("totp", (await AuthApi.JsonAsync(await AuthApi.PasswordAsync(browser, username, password, returnUrl))).GetProperty("next").GetString());
        var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/totp", AuthApi.ReissuedToken(browser),
            new { code = Totp.Code(secret, Totp.StepStillAccepted(ServerNow(), step)), returnUrl }));

        Assert.Equal("/Secrets", signedIn.GetProperty("redirect").GetString());
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/Secrets")).StatusCode);
    }

    [Theory]
    [InlineData("/Account/Totp", "/Secrets")]
    [InlineData("/Account/RecoveryCode", "/Secrets")]
    [InlineData("/Account/EnrollTotp", "/Secrets")]
    [InlineData("/account/TOTP/", "/Secrets")]
    [InlineData("/Account/Totp?ReturnUrl=%2FAccess", "/Secrets")]
    [InlineData("/Account/Totp/1#code", "/Secrets")]
    [InlineData("/Account\\Totp", "/Secrets")]
    [InlineData("~/Account/Totp", "/Secrets")]
    [InlineData("/Account//Totp", "/Secrets")]
    [InlineData("/Secrets/../Account/Totp", "/Secrets")]
    [InlineData("/Secrets\\..\\Account\\Totp", "/Secrets")]
    [InlineData("/Secrets/%2e%2e/Account/Totp", "/Secrets")]
    // Kestrel keeps an encoded slash inside one route segment rather than treating it as a separator.
    [InlineData("/Account/Totp/%2F..", "/Secrets")]
    [InlineData("/Account/Totp/..%2F", "/Secrets")]
    [InlineData("/Account/Totp/x%2F..%2F..", "/Secrets")]
    [InlineData("/Account/RecoveryCode/%2F..", "/Secrets")]
    [InlineData("/Account/EnrollTotp/..%2F", "/Secrets")]
    [InlineData("/Account/Totp/%252F..", "/Secrets")]
    [InlineData("/%2541ccount/Totp", "/Secrets")]
    [InlineData("/Account%252FTotp", "/Secrets")]
    [InlineData("/%41ccount/Totp", "/Secrets")]
    [InlineData("/%41ccount/%54otp", "/Secrets")]
    [InlineData("/Account%2FTotp", "/Secrets")]
    [InlineData("/Account", "/Secrets")]
    [InlineData("/Account;x/Totp", "/Secrets")]
    [InlineData("/Account/Totpx", "/Secrets")]
    [InlineData("/Account/Recover", "/Secrets")]
    [InlineData("/Account/Login?ReturnUrl=%2FAccess", "/Secrets")]
    [InlineData("/Account/Denied", "/Account/Denied")]
    [InlineData("/Account/Denied?x=1", "/Account/Denied?x=1")]
    [InlineData("/Account/Denied#top", "/Account/Denied#top")]
    [InlineData("/Access?view=mine", "/Access?view=mine")]
    // Location is ASCII-only; model binding decodes the first query, while the encoded query remains local.
    [InlineData("/Secrets?q=é", "/Secrets")]
    [InlineData("/Secrets?q=%C3%A9", "/Secrets?q=%C3%A9")]
    public async Task A_completed_sign_in_returns_neither_to_a_second_factor_page_nor_to_a_non_ascii_target(string target, string expected)
    {
        var (_, _, _, client, _) = await EnrollAccountAsync();
        using (client)
        {
            // Every Account page but Denied is refused as written, before any decoding, as is any path
            // that reads as something else once decoded or resolved. A second-factor page would loop.
            var response = await client.GetAsync(WithReturnUrl("/Account/Login", target));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(expected, response.Headers.Location?.OriginalString);
        }
    }

    [Fact]
    public async Task A_password_sign_in_without_a_return_url_lands_on_the_Secrets_list()
    {
        var (username, password, secret, enrolled, step) = await EnrollAccountAsync();
        enrolled.Dispose();
        var browser = NewSession();
        var token = await AuthApi.PendingAsync(browser, username, password);

        var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/totp", token,
            new { code = Totp.Code(secret, Totp.StepStillAccepted(ServerNow(), step)) }));

        Assert.Equal("/Secrets", signedIn.GetProperty("redirect").GetString());
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task A_non_ascii_target_falls_back_instead_of_failing_after_the_code_is_spent()
    {
        var (username, password, secret, enrolled, step) = await EnrollAccountAsync();
        enrolled.Dispose();
        var browser = NewSession();
        Assert.Equal("totp", (await AuthApi.JsonAsync(await AuthApi.PasswordAsync(browser, username, password, "/Secrets?q=é"))).GetProperty("next").GetString());

        var signedIn = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/totp", AuthApi.ReissuedToken(browser),
            new { code = Totp.Code(secret, Totp.StepStillAccepted(ServerNow(), step)), returnUrl = "/Secrets?q=é" }));

        Assert.Equal("/Secrets", signedIn.GetProperty("redirect").GetString());
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    // The target arrives as a JSON string, never transport-decoded; it is judged as written.
    [Theory]
    [InlineData("/Access?view=mine&sort=name#matrix", "/Access?view=mine&sort=name#matrix")]
    [InlineData(null, "/Secrets")]
    [InlineData("", "/Secrets")]
    [InlineData("https://outside.example/", "/Secrets")]
    [InlineData("//outside.example/", "/Secrets")]
    [InlineData("/\\outside.example/", "/Secrets")]
    [InlineData("\\\\outside.example/", "/Secrets")]
    [InlineData("/\t/outside.example", "/Secrets")]
    [InlineData("/\r\nLocation: https://outside.example", "/Secrets")]
    [InlineData("/\0/outside.example", "/Secrets")]
    [InlineData("https%3A%2F%2Foutside.example", "/Secrets")]
    [InlineData("%2f%2foutside.example", "/Secrets")]
    [InlineData("/%2foutside.example", "/Secrets")]
    [InlineData("/%5coutside.example", "/Secrets")]
    [InlineData("/folder\\child", "/folder\\child")]
    [InlineData("~/Access", "/Access")]
    [InlineData("/Account/Totp", "/Secrets")]
    [InlineData("/Secrets?q=é", "/Secrets")]
    public async Task Passkey_sign_in_uses_the_server_checked_target_only_after_a_valid_assertion(string? target, string expected)
    {
        using var authenticator = await EnrollWithPasskeyAsync();
        var browser = NewSession();
        var token = await AuthApi.AnonymousTokenAsync(browser, WithReturnUrl("/Account/Login", target));
        var signInOptions = (await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/begin", token,
            new { returnUrl = target }))).GetProperty("publicKey");
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);

        var result = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/complete", token,
            new { credential = authenticator.Assert(signInOptions) }));

        Assert.Equal(expected, result.GetProperty("redirect").GetString());
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
        // Consuming the challenge prevents a replay from issuing another redirect/session.
        var replay = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/complete", AuthApi.ReissuedToken(browser),
            new { credential = new { } }), HttpStatusCode.Conflict);
        Assert.Equal("sign_in_restarted", replay.GetProperty("code").GetString());
        Assert.False(replay.TryGetProperty("redirect", out _));
    }

    [Fact]
    public async Task A_failed_passkey_attempt_returns_no_target_and_a_retry_replaces_its_target()
    {
        using var authenticator = await EnrollWithPasskeyAsync();
        var browser = NewSession();
        var token = await AuthApi.AnonymousTokenAsync(browser, WithReturnUrl("/Account/Login", "/Access#matrix"));
        var timedOut = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/complete", token,
            new { credential = new { } }), HttpStatusCode.Conflict);
        Assert.False(timedOut.TryGetProperty("redirect", out _));
        token = AuthApi.ReissuedToken(browser);
        await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/begin", token, new { returnUrl = "/Access#matrix" }));
        var failed = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/complete", token,
            new { credential = new { } }), HttpStatusCode.BadRequest);
        Assert.False(failed.TryGetProperty("redirect", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);

        // A new attempt with no target must replace the failed attempt's destination.
        var retryOptions = (await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/begin", token, new { })))
            .GetProperty("publicKey");
        var retry = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/complete", token,
            new { credential = authenticator.Assert(retryOptions) }));

        Assert.Equal("/Secrets", retry.GetProperty("redirect").GetString());
    }

    /// <summary>A new account with a discoverable passkey registered through Profile.</summary>
    private async Task<PasskeyAuthenticator> EnrollWithPasskeyAsync()
    {
        var (_, password, secret, enrolled, _) = await EnrollAccountAsync();
        var authenticator = new PasskeyAuthenticator();
        using (enrolled)
        {
            var operationId = await VerifyProfileChangeAsync(enrolled, password, secret,
                new { kind = "AddPasskey", passkeyNickname = "Return URL test" });
            var options = await ProfileRequestAsync(enrolled, "/api/ui/profile/passkeys/registration/begin", new { operationId });
            var registration = await ProfileRequestAsync(enrolled, "/api/ui/profile/passkeys/registration/complete",
                new { operationId, credential = authenticator.Register(options.GetProperty("publicKey")) });
            Assert.NotEqual(Guid.Empty, registration.GetProperty("id").GetGuid());
        }
        return authenticator;
    }

    private static async Task<Guid> VerifyProfileChangeAsync(HttpClient client, string password, string secret, object change)
    {
        var prepared = await ProfileRequestAsync(client, "/api/ui/profile/operations", change);
        var operationId = prepared.GetProperty("operationId").GetGuid();
        await ProfileRequestAsync(client, $"/api/ui/profile/operations/{operationId}/verify",
            new { method = "PasswordAndAuthenticatorCode", currentPassword = password, code = Totp.NextCode(secret) });
        return operationId;
    }

    private static async Task<JsonElement> ProfileRequestAsync(HttpClient client, string path, object body)
    {
        var response = await UiApiRequest.SendWithTokenAsync(client, await UiApiRequest.AntiforgeryTokenAsync(client), HttpMethod.Post,
            path, JsonContent.Create(body));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(string Username, string Password, string Secret, HttpClient Client, long Step)> EnrollAccountAsync()
    {
        var (username, password) = await CreateAccountAsync();
        var browser = NewSession();
        var token = await AuthApi.PendingAsync(browser, username, password, next: "enroll");
        var secret = await EnrollmentSecretAsync(browser, token);
        // The verifier's own clock, read after enrollment began. The step it is on stays accepted if
        // the check happens in the next step; the previous step does not.
        var step = Totp.StepStillAccepted(ServerNow());
        var signedIn = await AuthApi.PostAsync(browser, "/api/ui/auth/enrollment/confirm", token, new { code = Totp.Code(secret, step) });
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        return (username, password, secret, browser.Client, step);
    }

    /// <summary>Begins enrollment as the EnrollTotp page does and returns the secret it shows.</summary>
    private static async Task<string> EnrollmentSecretAsync(VaultSession browser, string token)
    {
        var enrollment = await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/enrollment", token));
        var secret = enrollment.GetProperty("secretBase32").GetString();
        Assert.False(string.IsNullOrEmpty(secret));
        return secret;
    }

    private DateTimeOffset ServerNow() => vault.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    private VaultSession NewSession()
    {
        // These theories must not share another class's frozen credential-throttle budget.
        var sequence = Interlocked.Increment(ref _nextAddress);
        var high = (sequence >> 16).ToString("x", CultureInfo.InvariantCulture);
        var low = (sequence & ushort.MaxValue).ToString("x", CultureInfo.InvariantCulture);
        return vault.CreateSession(source: IPAddress.Parse($"2001:db8:91::{high}:{low}"));
    }

    private async Task<(string Username, string Password)> CreateAccountAsync()
    {
        var username = "return-" + Guid.NewGuid().ToString("N")[..12];
        var password = "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var admin = await vault.AdminClientAsync();
        var created = await UserAccounts.CreateAsync(admin, username, "Return", "Test", password);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (username, password);
    }

    private static string WithReturnUrl(string path, string? returnUrl) =>
        returnUrl is null ? path : path + "?ReturnUrl=" + Uri.EscapeDataString(returnUrl);
}
