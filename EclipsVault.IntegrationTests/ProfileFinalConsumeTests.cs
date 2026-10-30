using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class ProfileFinalConsumeTests(VaultFixture vault)
{
    [Fact]
    public async Task Two_authenticated_final_requests_commit_one_password_change_and_one_event()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-ui-final-race", "100.71.87.1");
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner,
            new { kind = "ChangePassword", newPassword = "Concurrent-New-Password-87!" });
        var before = await AuditTrail.HeadAsync(vault);
        await using var firstBody = new GatedRequestBody(JsonSerializer.Serialize(new { operationId }));
        await using var secondBody = new GatedRequestBody(JsonSerializer.Serialize(new { operationId }));
        var first = FinalRequestAsync(owner.Browser, firstBody);
        var second = FinalRequestAsync(owner.Browser, secondBody);
        try
        {
            await Task.WhenAll(firstBody.ReadStarted, secondBody.ReadStarted).WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            firstBody.Release();
            secondBody.Release();
        }

        var responses = await Task.WhenAll(first, second);

        Assert.Single(responses, response => response.Response.StatusCode == 204);
        Assert.Single(responses, response => response.Response.StatusCode is 404 or 409);
        Assert.Equal(owner.Stored.CredentialGeneration + 1, (await ProfileHttp.AccountAsync(vault, owner.Stored.Username)).CredentialGeneration);
        Assert.Equal([AuditAction.PasswordChanged], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
    }

    private Task<HttpContext> FinalRequestAsync(VaultSession browser, GatedRequestBody body)
        => vault.SendAsync(context =>
        {
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = "/api/ui/profile/password";
            context.Request.Method = "POST";
            context.Request.Headers.Cookie = browser.Cookies.GetCookieHeader(browser.Client.BaseAddress!);
            context.Request.Headers["RequestVerificationToken"] = browser.Cookies.GetCookies(browser.Client.BaseAddress!)["XSRF-TOKEN"]!.Value;
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = body.Length;
            context.Request.Body = body;
            context.Connection.RemoteIpAddress = IPAddress.Parse("100.71.87.1");
        });
}
