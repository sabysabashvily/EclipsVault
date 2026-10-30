using System.Net;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(RedisVaultCollection.Name)]
public sealed class ProfileSessionOutcomeTests(RedisVaultFixture fixture)
{
    [Fact]
    public async Task Profile_sign_out_reports_incomplete_revocation_and_clears_the_local_cookie_during_an_outage()
    {
        var owner = await ProfileAccount.CreateAsync(fixture.Vault, "it-ui87-ui-signout-outage", "100.70.87.1");
        await owner.Browser.Client.GetAsync("/api/ui/me");
        await using var body = new GatedRequestBody("__RequestVerificationToken=" + Uri.EscapeDataString(owner.Browser.Cookies.GetCookies(owner.Browser.Client.BaseAddress!)["XSRF-TOKEN"]!.Value));
        var request = SendGatedAsync(owner.Browser, "/api/ui/profile/sign-out-everywhere", body, "application/x-www-form-urlencoded", requestToken: null);
        try
        {
            await body.ReadStarted.WaitAsync(TimeSpan.FromSeconds(30));
            await fixture.Redis.PauseAsync();
            body.Release();

            var response = await request;

            Assert.Equal(503, response.Response.StatusCode);
            using var problem = await JsonDocument.ParseAsync(response.Response.Body);
            Assert.Equal("sign_out_incomplete", problem.RootElement.GetProperty("code").GetString());
            Assert.Contains(response.Response.Headers.SetCookie, cookie => cookie!.StartsWith("EclipsVault.Session=;", StringComparison.Ordinal));
            Assert.Contains("no-store", response.Response.Headers.CacheControl.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            body.Release();
            await fixture.Redis.ResumeAsync();
            await request;
        }
    }

    [Fact]
    public async Task A_committed_Profile_change_reports_sign_in_required_when_this_device_cannot_be_replaced()
    {
        var owner = await ProfileAccount.CreateAsync(fixture.Vault, "it-ui87-ui-replacement-refused", "100.70.87.2");
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner,
            new { kind = "ChangePassword", newPassword = "Committed-New-Password-87!" });
        var principal = SessionApi.PrincipalOf(fixture.Vault, owner.Browser);
        await using var body = new GatedRequestBody(JsonSerializer.Serialize(new { operationId }));
        var request = SendGatedAsync(owner.Browser, "/api/ui/profile/password", body, "application/json",
            owner.Browser.Cookies.GetCookies(owner.Browser.Client.BaseAddress!)["XSRF-TOKEN"]!.Value);
        try
        {
            await body.ReadStarted.WaitAsync(TimeSpan.FromSeconds(30));
            await fixture.Vault.Services.GetRequiredService<ISessionRegistry>().RevokeAsync(owner.Id, principal.GetSessionId()!.Value);
            body.Release();

            var response = await request;

            Assert.Equal(503, response.Response.StatusCode);
            using var problem = await JsonDocument.ParseAsync(response.Response.Body);
            Assert.Equal("changed_sign_in_required", problem.RootElement.GetProperty("code").GetString());
            Assert.Contains(response.Response.Headers.SetCookie, cookie => cookie!.StartsWith("EclipsVault.Session=;", StringComparison.Ordinal));
            var stored = await ProfileHttp.AccountAsync(fixture.Vault, owner.Stored.Username);
            Assert.Equal(owner.Stored.CredentialGeneration + 1, stored.CredentialGeneration);
            Assert.True(fixture.Vault.Services.GetRequiredService<IPasswordHasher>().Verify("Committed-New-Password-87!", stored.PasswordHash, stored.PasswordSalt));
        }
        finally
        {
            body.Release();
            await request;
        }
    }

    private Task<HttpContext> SendGatedAsync(VaultSession browser, string path, GatedRequestBody body, string contentType, string? requestToken)
        => fixture.Vault.SendAsync(context =>
        {
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = path;
            context.Request.Method = "POST";
            context.Request.Headers.Cookie = browser.Cookies.GetCookieHeader(browser.Client.BaseAddress!);
            context.Request.Headers["RequestVerificationToken"] = requestToken;
            context.Request.ContentType = contentType;
            context.Request.ContentLength = body.Length;
            context.Request.Body = body;
            context.Connection.RemoteIpAddress = IPAddress.Parse("100.70.87.3");
        });
}
