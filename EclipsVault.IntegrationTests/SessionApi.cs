using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

internal static class SessionApi
{
    public static async Task<HttpResponseMessage> WriteAsync(VaultSession session, HttpMethod method, string path)
    {
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/ui/me")).StatusCode);
        var token = Assert.IsType<Cookie>(session.Cookies.GetCookies(session.Client.BaseAddress!)["XSRF-TOKEN"]);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("RequestVerificationToken", token.Value);
        return await session.Client.SendAsync(request);
    }

    public static async Task<SessionResponse> CurrentAsync(VaultSession session)
    {
        var list = await session.Client.GetFromJsonAsync<SessionsResponse>("/api/ui/sessions");
        return Assert.Single(Assert.IsType<SessionsResponse>(list).Sessions, device => device.IsCurrent);
    }

    public static ClaimsPrincipal PrincipalOf(VaultFixture vault, VaultSession session)
    {
        var format = vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;
        return Assert.IsType<AuthenticationTicket>(format.Unprotect(Assert.IsType<Cookie>(session.SessionCookie).Value)).Principal;
    }

    public static Cookie WithoutAccountId(VaultFixture vault, VaultSession session)
    {
        var format = vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;
        var cookie = Assert.IsType<Cookie>(session.SessionCookie);
        var ticket = Assert.IsType<AuthenticationTicket>(format.Unprotect(cookie.Value));
        var identity = Assert.IsType<ClaimsIdentity>(ticket.Principal.Identity);
        identity.RemoveClaim(Assert.Single(identity.FindAll(ClaimTypes.NameIdentifier)));
        return new Cookie(cookie.Name, format.Protect(ticket));
    }
}
