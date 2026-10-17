using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed partial class SignInReturnUrlTests(VaultFixture vault)
{
    private static int _nextAddress;

    [Fact]
    public async Task Enrollment_completion_honors_a_local_target()
    {
        var (username, password) = await CreateAccountAsync();
        using var client = NewClient();
        var login = await client.GetStringAsync("/Account/Login");
        var afterPassword = await PostFormAsync(client, "/Account/Login", login, new()
        {
            ["Username"] = username, ["Password"] = password
        });
        Assert.Equal("/Account/EnrollTotp", afterPassword.Headers.Location?.OriginalString);
        var enrollment = await client.GetStringAsync("/Account/EnrollTotp");
        var secret = EnrollmentSecret().Match(enrollment).Groups[1].Value;
        Assert.NotEmpty(secret);
        var response = await PostFormAsync(client, "/Account/EnrollTotp", enrollment, new()
        {
            ["Code"] = Totp.Code(secret, Totp.CurrentStep()), ["ReturnUrl"] = "/Access"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Access", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/Access?view=mine&sort=name#matrix", "/Access?view=mine&sort=name#matrix")]
    [InlineData("https://outside.example/", "/Secrets")]
    public async Task Password_and_initial_enrollment_return_only_after_the_second_factor(string returnUrl, string expected)
    {
        var (username, password) = await CreateAccountAsync();
        using var client = NewClient();
        var loginPath = WithReturnUrl("/Account/Login", returnUrl);
        var login = await client.GetStringAsync(loginPath);
        Assert.Equal(returnUrl, HiddenReturnUrl(login));

        var afterPassword = await PostFormAsync(client, "/Account/Login", login, new()
        {
            ["Username"] = username, ["Password"] = password, ["ReturnUrl"] = HiddenReturnUrl(login)
        });
        Assert.Equal(WithReturnUrl("/Account/EnrollTotp", returnUrl), afterPassword.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/me")).StatusCode);

        var enrollment = await client.GetStringAsync(afterPassword.Headers.Location);
        Assert.Equal(returnUrl, HiddenReturnUrl(enrollment));
        var secret = EnrollmentSecret().Match(enrollment).Groups[1].Value;
        Assert.NotEmpty(secret);
        foreach (var invalidCode in new[] { "bad", Totp.Code(secret, Totp.CurrentStep() - 10) })
        {
            var invalid = await PostFormAsync(client, "/Account/EnrollTotp", enrollment, new()
            {
                ["Code"] = invalidCode, ["ReturnUrl"] = HiddenReturnUrl(enrollment)
            });
            Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
            enrollment = await invalid.Content.ReadAsStringAsync();
            Assert.Equal(returnUrl, HiddenReturnUrl(enrollment));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/me")).StatusCode);
        }
        var response = await PostFormAsync(client, "/Account/EnrollTotp", enrollment, new()
        {
            ["Code"] = Totp.Code(secret, Totp.CurrentStep()), ["ReturnUrl"] = HiddenReturnUrl(enrollment)
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Theory]
    [InlineData("/Access?view=mine&sort=name#matrix", "/Access?view=mine&sort=name#matrix")]
    [InlineData("https://outside.example/", "/Secrets")]
    public async Task Password_totp_and_alternate_methods_keep_the_target_across_errors(string returnUrl, string expected)
    {
        var (username, password, secret, enrolled) = await EnrollAccountAsync();
        enrolled.Dispose();
        using var client = NewClient();
        var login = await client.GetStringAsync(WithReturnUrl("/Account/Login", returnUrl));
        foreach (var invalidPassword in new[] { "", "incorrect-password" })
        {
            var invalid = await PostFormAsync(client, "/Account/Login", login, new()
            {
                ["Username"] = username, ["Password"] = invalidPassword, ["ReturnUrl"] = HiddenReturnUrl(login)
            });
            Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
            login = await invalid.Content.ReadAsStringAsync();
            Assert.Equal(returnUrl, HiddenReturnUrl(login));
        }

        var passwordResponse = await PostFormAsync(client, "/Account/Login", login, new()
        {
            ["Username"] = username, ["Password"] = password, ["ReturnUrl"] = HiddenReturnUrl(login)
        });
        Assert.Equal(WithReturnUrl("/Account/Totp", returnUrl), passwordResponse.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/me")).StatusCode);
        var totp = await client.GetStringAsync(passwordResponse.Headers.Location);
        foreach (var invalidCode in new[] { "bad", Totp.Code(secret, Totp.CurrentStep() - 10) })
        {
            var invalid = await PostFormAsync(client, "/Account/Totp", totp, new()
            {
                ["Code"] = invalidCode, ["ReturnUrl"] = HiddenReturnUrl(totp)
            });
            Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
            totp = await invalid.Content.ReadAsStringAsync();
            Assert.Equal(returnUrl, HiddenReturnUrl(totp));
        }

        var recoveryPath = LinkTo(totp, "RecoveryCode");
        Assert.Equal(WithReturnUrl("/Account/RecoveryCode", returnUrl), recoveryPath);
        var recovery = await client.GetStringAsync(recoveryPath);
        Assert.Equal(returnUrl, HiddenReturnUrl(recovery));
        var totpPath = LinkTo(recovery, "Totp");
        Assert.Equal(WithReturnUrl("/Account/Totp", returnUrl), totpPath);
        totp = await client.GetStringAsync(totpPath);
        var signedIn = await PostFormAsync(client, "/Account/Totp", totp, new()
        {
            ["Code"] = Totp.Code(secret, Totp.CurrentStep()), ["ReturnUrl"] = HiddenReturnUrl(totp)
        });
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal(expected, signedIn.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Theory]
    [InlineData("/Access", "/Access")]
    [InlineData("https://outside.example/", "/Secrets")]
    public async Task Recovery_code_completion_checks_the_target_and_creates_a_real_session(string returnUrl, string expected)
    {
        var (username, password, _, enrolled) = await EnrollAccountAsync();
        using (enrolled)
        {
            var generated = await PostFormAsync(enrolled, "/Profile/GenerateRecoveryCodes", await enrolled.GetStringAsync("/Profile"), new());
            Assert.Equal(HttpStatusCode.Redirect, generated.StatusCode);
            var codes = await enrolled.GetStringAsync(generated.Headers.Location);
            var code = RecoveryCode().Match(codes).Groups[1].Value;
            Assert.NotEmpty(code);

            using var client = NewClient();
            var afterPassword = await PostFormAsync(client, "/Account/Login", await client.GetStringAsync("/Account/Login"), new()
            {
                ["Username"] = username, ["Password"] = password, ["ReturnUrl"] = returnUrl
            });
            var recovery = await client.GetStringAsync(LinkTo(await client.GetStringAsync(afterPassword.Headers.Location), "RecoveryCode"));
            foreach (var invalidCode in new[] { "bad", "ZZZZZ-ZZZZZ" })
            {
                var invalid = await PostFormAsync(client, "/Account/RecoveryCode", recovery, new()
                {
                    ["Code"] = invalidCode, ["ReturnUrl"] = HiddenReturnUrl(recovery)
                });
                Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
                recovery = await invalid.Content.ReadAsStringAsync();
                Assert.Equal(returnUrl, HiddenReturnUrl(recovery));
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/me")).StatusCode);
            }
            var signedIn = await PostFormAsync(client, "/Account/RecoveryCode", recovery, new()
            {
                ["Code"] = code, ["ReturnUrl"] = HiddenReturnUrl(recovery)
            });
            Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
            Assert.Equal(expected, signedIn.Headers.Location?.OriginalString);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/me")).StatusCode);
        }
    }

    [Fact]
    public async Task A_second_factor_page_that_expired_mid_sign_in_does_not_become_a_redirect_loop()
    {
        var (username, password, secret, enrolled) = await EnrollAccountAsync();
        enrolled.Dispose();
        using var client = NewClient();
        // Past the MFA-pending cookie's six minutes, the second-factor page challenges like any
        // other page and names itself as the place to come back to.
        var challenge = await client.GetAsync("/Account/Totp");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var login = await client.GetStringAsync(challenge.Headers.Location);
        Assert.Equal("/Account/Totp", HiddenReturnUrl(login));

        var afterPassword = await PostFormAsync(client, "/Account/Login", login, new()
        {
            ["Username"] = username, ["Password"] = password, ["ReturnUrl"] = HiddenReturnUrl(login)
        });
        var totp = await client.GetStringAsync(afterPassword.Headers.Location);
        var response = await PostFormAsync(client, "/Account/Totp", totp, new()
        {
            ["Code"] = Totp.Code(secret, Totp.CurrentStep()), ["ReturnUrl"] = HiddenReturnUrl(totp)
        });
        for (var hops = 0; response.StatusCode == HttpStatusCode.Redirect; hops++)
        {
            Assert.True(hops < 5, "Sign-in redirects in a loop through " + response.Headers.Location);
            response = await client.GetAsync(response.Headers.Location);
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Secrets", response.RequestMessage?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task A_completed_sign_in_returns_neither_to_a_second_factor_page_nor_to_a_non_ascii_target()
    {
        var (_, _, _, client) = await EnrollAccountAsync();
        using (client)
        {
            // Every Account page but Denied is refused as written, before any decoding, as is any path
            // that reads as something else once decoded or resolved. A second-factor page would loop.
            (string Target, string Expected)[] targets =
            [
                ("/Account/Totp", "/Secrets"), ("/Account/RecoveryCode", "/Secrets"), ("/Account/EnrollTotp", "/Secrets"),
                ("/account/TOTP/", "/Secrets"), ("/Account/Totp?ReturnUrl=%2FAccess", "/Secrets"),
                ("/Account/Totp/1#code", "/Secrets"), ("/Account\\Totp", "/Secrets"), ("~/Account/Totp", "/Secrets"),
                ("/Account//Totp", "/Secrets"), ("/Secrets/../Account/Totp", "/Secrets"), ("/Secrets\\..\\Account\\Totp", "/Secrets"),
                ("/Secrets/%2e%2e/Account/Totp", "/Secrets"),
                // Kestrel keeps %2F inside a segment and the default route binds it to {id}.
                ("/Account/Totp/%2F..", "/Secrets"), ("/Account/Totp/..%2F", "/Secrets"),
                ("/Account/Totp/x%2F..%2F..", "/Secrets"), ("/Account/RecoveryCode/%2F..", "/Secrets"),
                ("/Account/EnrollTotp/..%2F", "/Secrets"),
                ("/Account/Totp/%252F..", "/Secrets"), ("/%2541ccount/Totp", "/Secrets"), ("/Account%252FTotp", "/Secrets"),
                ("/%41ccount/Totp", "/Secrets"), ("/%41ccount/%54otp", "/Secrets"), ("/Account%2FTotp", "/Secrets"),
                ("/Account", "/Secrets"), ("/Account;x/Totp", "/Secrets"), ("/Account/Totpx", "/Secrets"),
                ("/Account/Recover", "/Secrets"), ("/Account/Login?ReturnUrl=%2FAccess", "/Secrets"),
                ("/Account/Denied", "/Account/Denied"), ("/Account/Denied?x=1", "/Account/Denied?x=1"),
                ("/Account/Denied#top", "/Account/Denied#top"), ("/Access?view=mine", "/Access?view=mine"),
                // Location carries only ASCII. Model binding decodes %C3%A9 to é, which must not
                // reach LocalRedirect; the still-encoded query is an ordinary local target.
                ("/Secrets?q=é", "/Secrets"), ("/Secrets?q=%C3%A9", "/Secrets?q=%C3%A9")
            ];
            var wrong = new List<string>();
            foreach (var (target, expected) in targets)
            {
                var response = await client.GetAsync(WithReturnUrl("/Account/Login", target));
                if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location?.OriginalString != expected)
                {
                    wrong.Add($"{target} answered {(int)response.StatusCode} {response.Headers.Location}");
                }
            }
            Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
        }
    }

    [Fact]
    public async Task A_non_ascii_target_falls_back_instead_of_failing_after_the_code_is_spent()
    {
        var (username, password, secret, enrolled) = await EnrollAccountAsync();
        enrolled.Dispose();
        using var client = NewClient();
        var afterPassword = await PostFormAsync(client, "/Account/Login", await client.GetStringAsync("/Account/Login"), new()
        {
            ["Username"] = username, ["Password"] = password, ["ReturnUrl"] = "/Secrets?q=é"
        });
        var totp = await client.GetStringAsync(afterPassword.Headers.Location);
        Assert.Equal("/Secrets?q=é", HiddenReturnUrl(totp));
        var signedIn = await PostFormAsync(client, "/Account/Totp", totp, new()
        {
            ["Code"] = Totp.Code(secret, Totp.CurrentStep()), ["ReturnUrl"] = HiddenReturnUrl(totp)
        });
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/Secrets", signedIn.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Passkey_sign_in_uses_the_server_checked_target_only_after_a_valid_assertion()
    {
        var (username, _, _, enrolled) = await EnrollAccountAsync();
        using var authenticator = new PasskeyAuthenticator();
        using (enrolled)
        {
            var profile = await enrolled.GetStringAsync("/Profile");
            var options = await PostJsonAsync(enrolled, "/Profile/PasskeyRegisterBegin", profile, new { });
            var registration = await PostJsonAsync(enrolled, "/Profile/PasskeyRegisterComplete", profile, new
            {
                credential = authenticator.Register(options), nickname = "Return URL test"
            });
            Assert.True(registration.GetProperty("success").GetBoolean());
        }

        using (var failedClient = NewClient())
        {
            var login = await failedClient.GetStringAsync(WithReturnUrl("/Account/Login", "/Access#matrix"));
            var timedOut = await PostJsonAsync(failedClient, "/Account/PasskeyLoginComplete", login, new { credential = new { } });
            Assert.False(timedOut.GetProperty("success").GetBoolean());
            Assert.False(timedOut.TryGetProperty("redirect", out _));
            await PostJsonAsync(failedClient, "/Account/PasskeyLoginBegin", login, new { username, returnUrl = "/Access#matrix" });
            var failed = await PostJsonAsync(failedClient, "/Account/PasskeyLoginComplete", login, new { credential = new { } });
            Assert.False(failed.GetProperty("success").GetBoolean());
            Assert.False(failed.TryGetProperty("redirect", out _));
            Assert.Equal(HttpStatusCode.Unauthorized, (await failedClient.GetAsync("/api/ui/me")).StatusCode);
            // A new attempt with no target must replace the failed attempt's destination.
            var retryOptions = await PostJsonAsync(failedClient, "/Account/PasskeyLoginBegin", login, new { username });
            var retry = await PostJsonAsync(failedClient, "/Account/PasskeyLoginComplete", login, new { credential = authenticator.Assert(retryOptions) });
            Assert.True(retry.GetProperty("success").GetBoolean());
            var final = await failedClient.GetAsync(retry.GetProperty("redirect").GetString());
            Assert.Equal("/Secrets", final.Headers.Location?.OriginalString);
        }

        // Model binding decodes the transport once. A path that is still percent-encoded after that is
        // refused rather than interpreted, so an encoded separator never becomes an authority.
        (string? Target, string Expected)[] targets =
        [
            ("/Access?view=mine&sort=name#matrix", "/Access?view=mine&sort=name#matrix"),
            (null, "/Secrets"), ("", "/Secrets"), ("https://outside.example/", "/Secrets"),
            ("//outside.example/", "/Secrets"), ("/\\outside.example/", "/Secrets"),
            ("\\\\outside.example/", "/Secrets"), ("/\t/outside.example", "/Secrets"),
            ("/\r\nLocation: https://outside.example", "/Secrets"), ("/\0/outside.example", "/Secrets"),
            ("https%3A%2F%2Foutside.example", "/Secrets"), ("%2f%2foutside.example", "/Secrets"),
            ("/%2foutside.example", "/Secrets"), ("/%5coutside.example", "/Secrets"),
            ("/folder\\child", "/folder\\child"), ("~/Access", "/Access"),
            ("/Account/Totp", "/Secrets"), ("/Secrets?q=é", "/Secrets")
        ];
        foreach (var (target, expected) in targets)
        {
            using var client = NewClient();
            var login = await client.GetStringAsync(target is null ? "/Account/Login" : WithReturnUrl("/Account/Login", target));
            var options = await PostJsonAsync(client, "/Account/PasskeyLoginBegin", login, new { username, returnUrl = target });
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/me")).StatusCode);
            var result = await PostJsonAsync(client, "/Account/PasskeyLoginComplete", login, new { credential = authenticator.Assert(options) });
            Assert.True(result.GetProperty("success").GetBoolean());
            var redirect = result.GetProperty("redirect").GetString();
            Assert.StartsWith("/Account/Login?ReturnUrl=", redirect, StringComparison.Ordinal);
            var final = await client.GetAsync(redirect);
            Assert.Equal(HttpStatusCode.Redirect, final.StatusCode);
            Assert.Equal(expected, final.Headers.Location?.OriginalString);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/me")).StatusCode);
            // Consuming the challenge prevents a replay from issuing another redirect/session.
            var replay = await PostJsonAsync(client, "/Account/PasskeyLoginComplete", await client.GetStringAsync("/Profile"), new { credential = new { } });
            Assert.False(replay.GetProperty("success").GetBoolean());
            Assert.False(replay.TryGetProperty("redirect", out _));
        }
    }

    private async Task<(string Username, string Password, string Secret, HttpClient Client)> EnrollAccountAsync()
    {
        var (username, password) = await CreateAccountAsync();
        var client = NewClient();
        var afterPassword = await PostFormAsync(client, "/Account/Login", await client.GetStringAsync("/Account/Login"), new()
        {
            ["Username"] = username, ["Password"] = password
        });
        Assert.Equal("/Account/EnrollTotp", afterPassword.Headers.Location?.OriginalString);
        var enrollment = await client.GetStringAsync("/Account/EnrollTotp");
        var secret = EnrollmentSecret().Match(enrollment).Groups[1].Value;
        Assert.NotEmpty(secret);
        var signedIn = await PostFormAsync(client, "/Account/EnrollTotp", enrollment, new()
        {
            // Use the previous accepted step so a subsequent ordinary sign-in can use the current
            // one without replaying enrollment's code or sleeping through a whole authenticator step.
            ["Code"] = Totp.Code(secret, Totp.CurrentStep() - 1)
        });
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        return (username, password, secret, client);
    }

    private static string LinkTo(string page, string action)
    {
        var match = Regex.Match(page, "href=\"(/Account/" + action + "[^\"]*)\"");
        Assert.True(match.Success, "Missing alternate sign-in method.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<JsonElement> PostJsonAsync(HttpClient client, string path, string page, object body)
    {
        var response = await SendJsonAsync(client, path, page, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, string path, string page, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", VaultFixture.FormTokenOf(page, path));
        return await client.SendAsync(request);
    }

    private HttpClient NewClient() => vault.CreateSession(source: IPAddress.Parse(
        $"192.0.2.{Interlocked.Increment(ref _nextAddress).ToString(CultureInfo.InvariantCulture)}")).Client;

    private async Task<(string Username, string Password)> CreateAccountAsync()
    {
        var username = "return-" + Guid.NewGuid().ToString("N")[..12];
        var password = "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var admin = await vault.AdminClientAsync();
        var created = await PostFormAsync(admin, "/Admin/CreateUser", await admin.GetStringAsync("/Admin/CreateUser"), new()
        {
            ["Username"] = username, ["FirstName"] = "Return", ["LastName"] = "Test",
            ["Password"] = password, ["Clearance"] = "1", ["ProjectKey"] = "GLOBAL"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        return (username, password);
    }

    private static string WithReturnUrl(string path, string returnUrl) => path + "?ReturnUrl=" + Uri.EscapeDataString(returnUrl);

    private static string HiddenReturnUrl(string page)
    {
        var match = ReturnUrlField().Match(page);
        Assert.True(match.Success, "The sign-in form lost ReturnUrl.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = VaultFixture.FormTokenOf(page, path);
        return client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    [GeneratedRegex("<input[^>]*name=\"ReturnUrl\"[^>]*value=\"([^\"]*)\"")]
    private static partial Regex ReturnUrlField();

    [GeneratedRegex("<code class=\"secret-code\">([A-Z2-7=]+)</code>")]
    private static partial Regex EnrollmentSecret();

    [GeneratedRegex("<li class=\"mono\">([A-Z0-9-]+)</li>")]
    private static partial Regex RecoveryCode();
}
