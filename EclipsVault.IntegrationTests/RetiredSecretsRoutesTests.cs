using System.Net;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Secrets list and New secret are Angular pages; the Legacy search and create have no handler
/// left. Each row signs in an account of its own, so no row spends another's sign-in budget.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class RetiredSecretsRoutesTests(VaultFixture vault)
{
    [Theory]
    [InlineData("/Secrets", "it-secrets-page-1", "198.18.164.1")]
    [InlineData("/sEcReTs/InDeX", "it-secrets-page-2", "198.18.164.2")]
    [InlineData("/SECRETS/CREATE", "it-secrets-page-3", "198.18.164.3")]
    public async Task Signed_in_the_secrets_paths_serve_the_Angular_document(string path, string username, string source)
    {
        var member = await vault.SignInNewAccountAsync(username, IPAddress.Parse(source));

        var document = await member.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.True(document.Headers.CacheControl?.NoStore);
        Assert.Contains("<app-root>", await document.Content.ReadAsStringAsync());
        Assert.Contains("require-trusted-types-for 'script'", Assert.Single(document.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Signed_out_the_secrets_list_redirects_to_sign_in()
    {
        var response = await vault.CreateClient().GetAsync("/Secrets");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/Account/Login?ReturnUrl=", response.Headers.Location?.AbsoluteUri);
    }

    [Theory]
    [InlineData("GET", "/Secrets/Search?q=it", HttpStatusCode.NotFound, "it-retired-secrets-11", "198.18.164.11", "it-retired-secret-11")]
    [InlineData("GET", "/sEcReTs/sEaRcH?q=it", HttpStatusCode.NotFound, "it-retired-secrets-12", "198.18.164.12", "it-retired-secret-12")]
    [InlineData("POST", "/Secrets/Search", HttpStatusCode.NotFound, "it-retired-secrets-13", "198.18.164.13", "it-retired-secret-13")]
    [InlineData("POST", "/Secrets/Create", HttpStatusCode.MethodNotAllowed, "it-retired-secrets-14", "198.18.164.14", "it-retired-secret-14")]
    [InlineData("POST", "/sEcReTs/cReAtE/", HttpStatusCode.MethodNotAllowed, "it-retired-secrets-15", "198.18.164.15", "it-retired-secret-15")]
    [InlineData("POST", "/Secrets/Create/legacy", HttpStatusCode.NotFound, "it-retired-secrets-16", "198.18.164.16", "it-retired-secret-16")]
    [InlineData("GET", "/Secrets/Index/legacy", HttpStatusCode.NotFound, "it-retired-secrets-17", "198.18.164.17", "it-retired-secret-17")]
    public async Task The_retired_Legacy_search_and_create_reach_no_handler_even_with_a_session_and_antiforgery(
        string method, string path, HttpStatusCode status, string username, string source, string name)
    {
        var member = await vault.SignInNewAccountAsync(username, IPAddress.Parse(source));

        var response = await UiApiRequest.WriteWithAntiforgeryAsync(member, new HttpMethod(method), path,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Name"] = name,
                ["Value"] = "it-retired-value",
                ["ProjectKey"] = "GLOBAL",
                ["Environment"] = "Development",
                ["Sensitivity"] = "Internal",
            }));

        Assert.Equal(status, response.StatusCode);
        using var scope = vault.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.AnyAsync(secret => secret.Name == name));
    }
}
