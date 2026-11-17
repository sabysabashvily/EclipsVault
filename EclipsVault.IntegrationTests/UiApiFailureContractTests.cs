using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The UI API's failure contract (ADR-0006, ADR-0013): every failure under <c>/api/ui/</c> is an
/// RFC 9457 Problem Details response with a stable code and no exception detail, and every response
/// there is <c>no-store</c>. The Legacy UI keeps its redirects and the Public API keeps its own shape.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class UiApiFailureContractTests(VaultFixture vault)
{
    /// <summary>The only members a UI API problem may carry: nothing an exception could fill.</summary>
    private static readonly string[] ProblemMembers = ["code", "detail", "status", "title", "type"];

    [Theory]
    [InlineData("audit", 503, "audit_unavailable")]
    [InlineData("not_found", 404, "not_found")]
    [InlineData("legacy_blob", 409, "legacy_blob_refused")]
    [InlineData("crypto", 500, "crypto_unavailable")]
    [InlineData("premium", 402, "premium_not_licensed")]
    [InlineData("domain", 400, "invalid_request")]
    [InlineData("unhandled", 500, "internal_error")]
    public async Task Each_failure_on_the_ui_api_is_a_problem_with_its_status_and_code(string kind, int status, string code)
    {
        var admin = await vault.AdminClientAsync();

        var response = await admin.GetAsync($"/api/ui/test-only/throw/{kind}");

        await AssertProblemAsync(response, status, code);
    }

    [Theory]
    [InlineData("audit", "/Home/Error?code=503")]
    [InlineData("not_found", "/Home/Error?code=404")]
    [InlineData("legacy_blob", "/Home/Error?code=409")]
    [InlineData("crypto", "/Home/Error?code=500")]
    [InlineData("premium", "/Home/Error?code=402")]
    [InlineData("domain", "/Home/Error?code=400")]
    [InlineData("unhandled", "/Home/Error?code=500")]
    public async Task Each_failure_on_the_legacy_ui_still_redirects_to_its_error_page(string kind, string errorPage)
    {
        var admin = await vault.AdminClientAsync();

        var response = await admin.GetAsync($"/test-only/throw/{kind}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(errorPage, response.Headers.Location?.OriginalString);
        Assert.Empty(await response.Content.ReadAsStringAsync());
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
    public async Task A_honey_token_on_the_legacy_ui_still_signs_out_to_the_sign_in_page()
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
        var session = await vault.SignInNewAccountAsync("it-revoked");
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
    public async Task A_blacklisted_source_gets_a_vague_403_problem_on_the_ui_api_and_the_block_page_elsewhere()
    {
        // TEST-NET-3 (RFC 5737): never a real caller, and no other test sends from it.
        var source = IPAddress.Parse("203.0.113.46");
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync(source.ToString(), "Integration test block");
        try
        {
            var uiApi = await vault.SendFromAsync(source, "/api/ui/me");
            var legacy = await vault.SendFromAsync(source, "/Account/Login");

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

            Assert.Equal(StatusCodes.Status403Forbidden, legacy.Response.StatusCode);
            Assert.StartsWith("text/html", legacy.Response.ContentType);
            Assert.Contains("<h1>Access blocked</h1>", await new StreamReader(legacy.Response.Body).ReadToEndAsync());
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
    public async Task The_public_api_keeps_its_own_error_shape()
    {
        var apiKey = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-public-api-shape");
        var service = vault.CreateClient();
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var notFound = await service.GetAsync($"/api/v1/secrets/{Guid.NewGuid()}");
        var noKey = await vault.CreateClient().GetAsync("/api/v1/secrets");

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal("application/json", notFound.Content.Headers.ContentType?.MediaType);
        Assert.Equal("""{"error":"not_found"}""", await notFound.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);
        Assert.Empty(await noKey.Content.ReadAsStringAsync());
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

        Assert.DoesNotContain(TestOnlyFailures.Detail, body);
        // No exception type name and no stack frame, both of which name the vault's namespaces.
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("EclipsVault.", body);
    }

    private static void AssertOnlyProblemMembers(JsonDocument problem)
        => Assert.All(problem.RootElement.EnumerateObject(), member => Assert.Contains(member.Name, ProblemMembers));
}
