using System.Net;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Angular sign-in pages as a browser loads them. Each document sets the request token its calls
/// carry, and each change of identity reissues it (ADR-0032).
/// </summary>
internal static class SignInDocument
{
    /// <summary>Loads a sign-in page and returns the request token it set.</summary>
    public static async Task<string> TokenAsync(HttpClient client, string path = "/Account/Login")
    {
        using var page = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return TokenOf(page);
    }

    /// <summary>Notices for sign-in travel as a query code or their own cookie, never as a TempData flash.</summary>
    public static void AssertNoTempData(HttpResponseMessage response)
        => Assert.DoesNotContain(response.Headers.TryGetValues(HeaderNames.SetCookie, out var cookies) ? cookies : [],
            cookie => cookie.StartsWith(".AspNetCore.Mvc.CookieTempDataProvider=", StringComparison.Ordinal));

    /// <summary>The request token a response set, as the Angular UI reads it from its cookie.</summary>
    public static string TokenOf(HttpResponseMessage response)
        => Assert.Single(SetCookieHeaderValue.ParseList(response.Headers.GetValues(HeaderNames.SetCookie).ToList()),
            cookie => cookie.Name == "XSRF-TOKEN").Value.ToString();
}
