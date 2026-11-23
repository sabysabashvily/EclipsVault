using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Every failure answers in its caller's format, without a redirect and without exception detail
/// (ADR-0006, ADR-0013, ADR-0034). Under <c>/api/ui/</c> it is an RFC 9457 Problem Details response
/// with a stable code, under <c>/api/v1/</c> it is <c>{ "error": "&lt;code&gt;" }</c>, and anywhere
/// else it is the error server page. Every <c>/api/</c> response is <c>no-store</c>.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class UiApiFailureContractTests(VaultFixture vault)
{
    /// <summary>The only members a UI API problem may carry: nothing an exception could fill.</summary>
    private static readonly string[] ProblemMembers = ["code", "detail", "status", "title", "type"];

    /// <summary>Each failure the middleware maps: its kind, status and code.</summary>
    public static TheoryData<string, int, string> Codes => new()
    {
        { "audit", 503, "audit_unavailable" },
        { "session_store", 503, "session_store_unavailable" },
        { "unblock_unrecorded", 503, "unblock_unrecorded" },
        { "recovery_incomplete", 503, "recovery_incomplete" },
        { "not_found", 404, "not_found" },
        { "legacy_blob", 409, "legacy_blob_refused" },
        { "crypto", 500, "crypto_unavailable" },
        { "premium", 402, "premium_not_licensed" },
        { "domain", 400, "invalid_request" },
        { "unhandled", 500, "internal_error" },
    };

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task Each_failure_on_the_ui_api_is_a_problem_with_its_status_and_code(string kind, int status, string code)
    {
        var admin = await vault.AdminClientAsync();

        var response = await admin.GetAsync($"/api/ui/test-only/throw/{kind}");

        await AssertProblemAsync(response, status, code);
    }

    [Theory]
    [InlineData("audit", 503, "Vault unavailable (fail-closed)", "The audit trail is unavailable, so the vault refused the request.")]
    [InlineData("session_store", 503, "Vault unavailable (fail-closed)", "The session store is unavailable, so the vault refused the change. Nothing was saved.")]
    [InlineData("unblock_unrecorded", 503, "Vault unavailable (fail-closed)", "The block was lifted, but recording and restoring it failed. Check the range before retrying.")]
    [InlineData("recovery_incomplete", 503, "Vault unavailable (fail-closed)", "Your sign-in was recorded, but the vault could not finish lifting the block, so this network stays blocked. From another network, an administrator can release it under Administration → Networks.")]
    [InlineData("not_found", 404, "Not found", "Not found.")]
    [InlineData("legacy_blob", 409, "Secret needs a one-time upgrade", "This secret is sealed in a format the vault refuses until an administrator re-seals it.")]
    [InlineData("crypto", 500, "Something went wrong", "The vault's encryption is unavailable.")]
    [InlineData("premium", 402, "Licence required", "This feature needs a licence.")]
    [InlineData("domain", 400, "Something went wrong", "The request could not be completed.")]
    [InlineData("unhandled", 500, "Something went wrong", "Something went wrong.")]
    public async Task Each_failure_outside_the_apis_is_the_error_page_with_its_status_and_no_redirect(
        string kind, int status, string heading, string line)
    {
        var admin = await vault.AdminClientAsync();

        var response = await admin.GetAsync($"/test-only/throw/{kind}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "The page is not no-store");
        Assert.Contains("require-trusted-types-for 'script'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        var text = WebUtility.HtmlDecode(body);
        Assert.Contains($"<title>{heading} — EclipsVault</title>", text);
        Assert.Contains($"<h1>{status} — {heading}</h1>", text);
        Assert.Contains(line, text, StringComparison.Ordinal);
        Assert.Contains("Request id: <code>", text);
        Assert.Matches("""<a\b[^>]*\bhref="/"[^>]*>Back to safety</a>""", text);
        Assert.DoesNotContain("<script", text);
        AssertNoExceptionDetail(body);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task Each_failure_on_the_public_api_is_its_error_code_with_its_status(string kind, int status, string code)
    {
        var service = await PublicApiClientAsync($"it-public-fail-{kind}");

        var response = await service.GetAsync($"/api/v1/test-only/throw/{kind}");

        await AssertPublicErrorAsync(response, status, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("evk_invalid")]
    public async Task A_public_API_request_without_a_valid_key_gets_the_same_JSON_challenge(string? key)
    {
        using var client = vault.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/secrets");
        request.Headers.Authorization = key is null ? null : new AuthenticationHeaderValue("Bearer", key);

        using var response = await client.SendAsync(request);

        await AssertPublicErrorAsync(response, 401, "unauthenticated");
    }

    [Fact]
    public async Task A_honey_token_on_the_public_api_answers_an_ordinary_not_found()
    {
        var notFound = await (await PublicApiClientAsync("it-public-honey-control")).GetAsync("/api/v1/test-only/throw/not_found");
        var intruder = await PublicApiClientAsync("it-public-honey");

        var response = await intruder.GetAsync("/api/v1/test-only/throw/honey_token");

        await AssertPublicErrorAsync(response, 404, "not_found");
        Assert.Equal(HeadersOf(notFound), HeadersOf(response));
    }

    [Fact]
    public async Task Every_public_api_response_is_no_store()
    {
        var service = await PublicApiClientAsync("it-public-no-store");

        HttpResponseMessage[] responses =
        [
            await service.GetAsync("/api/v1/secrets"),
            await service.GetAsync($"/api/v1/secrets/{Guid.NewGuid()}"),
            await vault.CreateClient().GetAsync("/api/v1/secrets"),
            await service.GetAsync("/api/v1/test-only/throw/unhandled"),
        ];

        Assert.All(responses, r => Assert.True(r.Headers.CacheControl?.NoStore, $"{r.RequestMessage?.RequestUri} is not no-store"));
        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.InternalServerError },
            responses.Select(r => r.StatusCode));
    }

    private async Task<HttpClient> PublicApiClientAsync(string serviceAccountName)
    {
        var client = vault.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), serviceAccountName));
        return client;
    }

    private static async Task AssertPublicErrorAsync(HttpResponseMessage response, int status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "The response is not no-store");
        Assert.Equal($$"""{"error":"{{code}}"}""", body);
    }

    [Fact]
    public async Task A_honey_token_on_the_ui_api_answers_an_ordinary_not_found_and_its_session_is_revoked()
    {
        var notFound = await (await vault.AdminClientAsync()).GetAsync("/api/ui/test-only/throw/not_found");
        var intruder = await vault.SignInNewAccountAsync("it-honey-ui");

        var response = await intruder.Client.GetAsync("/api/ui/test-only/throw/honey_token");

        await AssertProblemAsync(response, 404, "not_found");
        Assert.Equal(await notFound.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        Assert.Equal(HeadersOf(notFound), HeadersOf(response));
        Assert.NotNull(intruder.SessionCookie);

        // Nothing in the answer told the intruder, but the session is already revoked on the server.
        var next = await intruder.Client.GetAsync("/api/ui/me");
        await AssertProblemAsync(next, 401, "unauthenticated");
        Assert.Null(intruder.SessionCookie);
    }

    /// <summary>Every header but the date, which differs between any two responses.</summary>
    private static string[] HeadersOf(HttpResponseMessage response)
        => response.Headers.Concat(response.Content.Headers)
            .Where(header => header.Key != "Date")
            .Select(header => $"{header.Key}: {string.Join(", ", header.Value)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public async Task A_honey_token_outside_the_apis_still_signs_out_to_the_sign_in_page()
    {
        var intruder = await vault.SignInNewAccountAsync("it-honey-legacy");
        var stolenCookie = Assert.IsType<Cookie>(intruder.SessionCookie);

        var response = await intruder.Client.GetAsync("/test-only/throw/honey_token");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
        Assert.Null(intruder.SessionCookie);
        var replayed = await vault.CreateSession(stolenCookie).Client.GetAsync("/Secrets");
        Assert.Equal(HttpStatusCode.Redirect, replayed.StatusCode);
        Assert.StartsWith("https://localhost/Account/Login?ReturnUrl=", replayed.Headers.Location?.AbsoluteUri);
    }

    [Fact]
    public async Task A_request_without_a_session_gets_401_not_a_redirect()
    {
        var response = await vault.CreateClient().GetAsync("/api/ui/me");

        await AssertProblemAsync(response, 401, "unauthenticated");
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task An_unknown_ui_api_path_is_a_not_found_problem_and_still_needs_a_session()
    {
        var admin = await vault.AdminClientAsync();

        await AssertProblemAsync(await admin.GetAsync("/api/ui/no-such-endpoint"), 404, "not_found");
        await AssertProblemAsync(await vault.CreateClient().GetAsync("/api/ui/no-such-endpoint"), 401, "unauthenticated");
    }

    [Fact]
    public async Task A_session_that_is_not_allowed_gets_403_not_a_redirect()
    {
        var admin = await vault.AdminClientAsync();

        var response = await admin.GetAsync("/api/ui/test-only/forbidden");

        await AssertProblemAsync(response, 403, "forbidden");
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task A_revoked_session_gets_401_on_its_next_request()
    {
        var session = await vault.SignInNewAccountAsync("it-revoked", IPAddress.Parse("2001:db8:175:1::4"));
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/ui/me")).StatusCode);

        await vault.RevokeAllSessionsOfAsync("it-revoked");
        var response = await session.Client.GetAsync("/api/ui/me");

        await AssertProblemAsync(response, 401, "unauthenticated");
        Assert.Null(session.SessionCookie);
    }

    [Fact]
    public async Task The_legacy_ui_keeps_its_sign_in_and_access_denied_redirects()
    {
        var admin = await vault.AdminClientAsync();

        var anonymous = await vault.CreateClient().GetAsync("/test-only/forbidden");
        var denied = await admin.GetAsync("/test-only/forbidden");

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal("https://localhost/Account/Login?ReturnUrl=%2Ftest-only%2Fforbidden", anonymous.Headers.Location?.AbsoluteUri);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("https://localhost/Account/Denied?ReturnUrl=%2Ftest-only%2Fforbidden", denied.Headers.Location?.AbsoluteUri);
    }

    [Fact]
    public async Task A_blacklisted_source_gets_a_vague_403_in_its_callers_format()
    {
        // TEST-NET-3 (RFC 5737): never a real caller, and no other test sends from it.
        var source = IPAddress.Parse("203.0.113.46");
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync(source.ToString(), "Integration test block");
        try
        {
            var uiApi = await vault.SendFromAsync(source, "/api/ui/me");
            var publicApi = await vault.SendFromAsync(source, "/api/v1/secrets");
            var page = await vault.SendFromAsync(source, "/Account/Login");
            var stylesheet = await vault.SendFromAsync(source, "/css/server-page.css");

            Assert.Equal(StatusCodes.Status403Forbidden, uiApi.Response.StatusCode);
            Assert.Equal("application/problem+json", uiApi.Response.ContentType);
            Assert.Contains("no-store", uiApi.Response.Headers.CacheControl.ToString());
            using var problem = JsonDocument.Parse(await new StreamReader(uiApi.Response.Body).ReadToEndAsync());
            AssertOnlyProblemMembers(problem);
            Assert.Equal("blocked", problem.RootElement.GetProperty("code").GetString());
            Assert.Equal("Access blocked", problem.RootElement.GetProperty("title").GetString());
            Assert.Equal(
                "Requests from your network location have been blocked by the vault's intrusion defence. " +
                "The event has been reported to the vault administrators.",
                problem.RootElement.GetProperty("detail").GetString());

            Assert.Equal(StatusCodes.Status403Forbidden, page.Response.StatusCode);
            Assert.StartsWith("text/html", page.Response.ContentType);
            Assert.Contains("require-trusted-types-for 'script'", page.Response.Headers.ContentSecurityPolicy.ToString());
            var html = await new StreamReader(page.Response.Body).ReadToEndAsync();
            Assert.Contains("<title>Access blocked — EclipsVault</title>", html);
            Assert.Contains("<h1>Access blocked</h1>", html);
            Assert.Contains("""<a class="button primary wide" href="/Account/Recover">Vault administrator? Recover access</a>""", html);
            Assert.Contains("Not an administrator? Ask one to lift the block from Administration → Networks.", html);
            Assert.Contains("""<link rel="stylesheet" href="/css/server-page.css" />""", html);
            Assert.DoesNotContain("<script", html);

            // Static files are served before the block, so the page keeps its look on a blocked network.
            Assert.Equal(StatusCodes.Status200OK, stylesheet.Response.StatusCode);
            Assert.StartsWith("text/css", stylesheet.Response.ContentType);

            Assert.Equal(StatusCodes.Status403Forbidden, publicApi.Response.StatusCode);
            Assert.StartsWith("application/json", publicApi.Response.ContentType);
            Assert.Contains("no-store", publicApi.Response.Headers.CacheControl.ToString());
            Assert.Equal("""{"error":"blocked"}""", await new StreamReader(publicApi.Response.Body).ReadToEndAsync());
        }
        finally
        {
            await (await blacklist.LiftForRecoveryAsync(source)).ReleaseAsync();
        }
    }

    [Fact]
    public async Task Every_ui_api_response_is_no_store()
    {
        var admin = await vault.AdminClientAsync();

        HttpResponseMessage[] responses =
        [
            await admin.GetAsync("/api/ui/me"),
            await admin.GetAsync("/api/ui/no-such-endpoint"),
            await vault.CreateClient().GetAsync("/api/ui/me"),
            await admin.GetAsync("/api/ui/test-only/throw/unhandled"),
        ];

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.InternalServerError },
            responses.Select(r => r.StatusCode));
        Assert.All(responses, r => Assert.True(r.Headers.CacheControl?.NoStore, $"{r.RequestMessage?.RequestUri} is not no-store"));
    }

    [Fact]
    public async Task A_valid_key_for_an_unknown_secret_gets_the_JSON_not_found()
    {
        using var service = await PublicApiClientAsync("it-public-api-shape");

        using var notFound = await service.GetAsync($"/api/v1/secrets/{Guid.NewGuid()}");

        await AssertPublicErrorAsync(notFound, 404, "not_found");
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, int status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "The response is not no-store");

        using var problem = JsonDocument.Parse(body);
        AssertOnlyProblemMembers(problem);
        Assert.Equal(status, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("title").GetString()));

        AssertNoExceptionDetail(body);
    }

    private static void AssertNoExceptionDetail(string body)
    {
        Assert.DoesNotContain(TestOnlyFailures.Detail, body);
        // No exception type name and no stack frame, both of which name the vault's namespaces.
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("EclipsVault.", body);
    }

    private static void AssertOnlyProblemMembers(JsonDocument problem)
        => Assert.All(problem.RootElement.EnumerateObject(), member => Assert.Contains(member.Name, ProblemMembers));
}
