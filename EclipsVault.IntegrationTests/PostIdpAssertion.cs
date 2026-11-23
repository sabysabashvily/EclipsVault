using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

internal static class PostIdpAssertion
{
    /// <summary>
    /// Arrives at the callback as if the identity provider had just verified <paramref name="email"/>.
    /// This begins after the provider's assertion, with the real correlation cookie scheme and its
    /// protector: it covers how the vault matches an account, not how it validates the provider's tokens.
    /// </summary>
    public static async Task<HttpResponseMessage> InvokeAsync(VaultFixture vault, string email, string source)
        => await InvokeAsync(vault, email, vault.CreateSession(source: IPAddress.Parse(source)));

    public static async Task<HttpResponseMessage> InvokeAsync(VaultFixture vault, string email, VaultSession callback, bool providerMfa = false)
    {
        var options = vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthSchemes.OidcCorrelation);
        var assertion = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("iss", "https://idp.example.test/realms/integration"),
            new Claim("sub", "it-email-" + Guid.NewGuid()),
            new Claim("email", email),
            new Claim("email_verified", "true"),
        ], AuthSchemes.Oidc));
        if (providerMfa)
        {
            ((ClaimsIdentity)assertion.Identity!).AddClaim(new Claim("amr", "mfa"));
        }

        var ticket = new AuthenticationTicket(assertion, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10),
        }, AuthSchemes.OidcCorrelation);
        var cookieName = Assert.IsType<string>(options.Cookie.Name);
        callback.Cookies.Add(callback.Client.BaseAddress!, new Cookie(cookieName, options.TicketDataFormat.Protect(ticket), "/")
        {
            Secure = true,
            HttpOnly = true,
        });

        return await callback.Client.GetAsync("/Account/ExternalCallback");
    }

    /// <summary>
    /// Where a successful callback sends the browser: a page of the vault's own that goes on at once, so
    /// the next request leaves the provider's cross-site redirect chain and carries the Strict cookies.
    /// </summary>
    public static async Task<string> ContinuesToAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var page = await response.Content.ReadAsStringAsync();
        var refresh = Assert.Single(Regex.Matches(page, "<meta http-equiv=\"refresh\" content=\"0;url=([^\"]+)\" />")).Groups[1].Value;
        Assert.Contains($"<a href=\"{refresh}\">Continue to the vault</a>", page, StringComparison.Ordinal);
        return WebUtility.HtmlDecode(refresh);
    }
}
