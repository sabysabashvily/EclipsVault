using System.Net;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A path the route table does not name is not a page (ADR-0034): each caller gets an honest
/// not-found in its own format, and a signed-out caller is still sent to sign-in.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class NotFoundTests(VaultFixture vault)
{
    [Theory]
    [InlineData("/no-such-page")]
    [InlineData("/Access/NotAnAngularRoute")]
    [InlineData("/Secrets/Details/not-a-guid")]
    [InlineData("/Home/Error")]
    public async Task Signed_in_an_unknown_path_is_the_not_found_page(string path)
    {
        using var response = await (await vault.AdminClientAsync()).GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>Page not found — EclipsVault</title>", html, StringComparison.Ordinal);
        Assert.Contains("<h1>404 — Page not found</h1>", html, StringComparison.Ordinal);
        Assert.Contains("There is no page at this address.", html, StringComparison.Ordinal);
        Assert.Contains("""<a class="button primary wide" href="/">Back to safety</a>""", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<app-root>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST", "/no-such-page", HttpStatusCode.NotFound)]
    [InlineData("DELETE", "/Admin/NoSuchAction/11111111-1111-1111-1111-111111111111", HttpStatusCode.NotFound)]
    [InlineData("POST", "/Access", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PUT", "/", HttpStatusCode.MethodNotAllowed)]
    public async Task Signed_in_any_method_on_an_unknown_path_is_not_found_and_on_a_page_is_not_allowed(
        string method, string path, HttpStatusCode expected)
    {
        var admin = await vault.AdminClientAsync();

        using var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin),
            new HttpMethod(method), path);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Signed_in_an_unknown_ui_api_path_is_a_problem_not_found()
        => await UiApiFailureContractTests.AssertProblemAsync(
            await (await vault.AdminClientAsync()).GetAsync("/api/ui/x"), 404, "not_found");

    [Theory]
    [InlineData("/ui/missing.js")]
    [InlineData("/css/missing.css")]
    [InlineData("/Access/missing.txt")]
    public async Task Signed_in_a_missing_file_is_a_bare_not_found(string path)
    {
        using var response = await (await vault.AdminClientAsync()).GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/no-such-page")]
    [InlineData("/ui/missing.js")]
    [InlineData("/")]
    public async Task Signed_out_a_page_or_file_is_sent_to_sign_in(string path)
    {
        using var response = await vault.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.Equal(path, System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query)["ReturnUrl"]);
    }

    [Fact]
    public async Task Signed_out_an_unknown_ui_api_path_is_unauthenticated()
        => await UiApiFailureContractTests.AssertProblemAsync(await vault.CreateClient().GetAsync("/api/ui/x"), 401, "unauthenticated");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unknown_public_api_path_is_a_JSON_not_found_signed_in_or_out(bool signedIn)
    {
        var client = signedIn ? await vault.AdminClientAsync() : vault.CreateClient();

        using var response = await client.GetAsync("/api/v1/x");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("""{"error":"not_found"}""", await response.Content.ReadAsStringAsync());
    }

    /// <summary>A method a Public API path does not accept is, like an unknown path, its JSON not-found.</summary>
    [Fact]
    public async Task A_public_api_path_with_a_method_it_does_not_accept_is_a_JSON_not_found()
    {
        using var response = await vault.CreateClient().DeleteAsync("/api/v1/secrets");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("""{"error":"not_found"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Signed_in_the_root_is_the_overview_document()
    {
        using var response = await (await vault.AdminClientAsync()).GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<app-root>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
