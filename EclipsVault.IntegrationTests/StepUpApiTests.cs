using System.Net;
using System.Net.Http.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class StepUpApiTests(VaultFixture vault)
{
    [Fact]
    public async Task A_real_code_renews_freshness_without_replacing_the_session_and_cannot_be_replayed()
    {
        var admin = await vault.SignInNewAccountAsync("it-step-renew", IPAddress.Parse("198.18.109.20"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.109.20"));
        var format = vault.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;
        var before = Assert.IsType<AuthenticationTicket>(format.Unprotect(stale.SessionCookie!.Value));
        var account = await ProfileHttp.AccountAsync(vault, "it-step-renew");
        var code = Totp.Code(account.TotpSecret!, Totp.CurrentStep() + 1);

        var response = await VerifyAsync(stale, code);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var after = Assert.IsType<AuthenticationTicket>(format.Unprotect(stale.SessionCookie!.Value));
        Assert.Equal(before.Principal.GetSessionId(), after.Principal.GetSessionId());
        Assert.Equal(before.Principal.GetSessionStampOrNull(), after.Principal.GetSessionStampOrNull());
        Assert.Equal(before.Properties.Items, after.Properties.Items);
        Assert.Equal(before.Principal.Claims.Where(claim => claim.Type != VaultClaimTypes.StepUpTime).Select(claim => (claim.Type, claim.Value)), after.Principal.Claims.Where(claim => claim.Type != VaultClaimTypes.StepUpTime).Select(claim => (claim.Type, claim.Value)));
        Assert.InRange(after.Principal.GetLastStrongAuthUtc(), DateTimeOffset.UtcNow.AddSeconds(-10), DateTimeOffset.UtcNow);
        await UiApiFailureContractTests.AssertProblemAsync(await UserActions.UnlockAsync(stale.Client, Guid.NewGuid()), 404, "user_not_found");
        await UiApiFailureContractTests.AssertProblemAsync(await VerifyAsync(stale, code), 400, "step_up_failed");
        Assert.Equal(1, (await ProfileHttp.AccountAsync(vault, "it-step-renew")).FailedAccessCount);
        await vault.RevokeAllSessionsOfAsync("it-step-renew");
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Wrong_codes_share_sign_in_failure_counting_and_lockout()
    {
        var owner = await vault.SignInNewAccountAsync("it-step-lockout", IPAddress.Parse("198.18.109.21"));
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.Users.Where(user => user.Username == "it-step-lockout").ExecuteUpdateAsync(change => change.SetProperty(user => user.FailedAccessCount, 4));

        var response = await VerifyAsync(owner, "invalid");

        await UiApiFailureContractTests.AssertProblemAsync(response, 400, "step_up_failed");
        Assert.Contains("That authenticator code is not valid. Try again.", await response.Content.ReadAsStringAsync());
        var account = await ProfileHttp.AccountAsync(vault, "it-step-lockout");
        Assert.Equal(0, account.FailedAccessCount);
        Assert.True(account.LockedUntilUtc > DateTimeOffset.UtcNow);
        await UiApiFailureContractTests.AssertProblemAsync(await VerifyAsync(owner, Totp.Code(account.TotpSecret!, Totp.CurrentStep() + 1)), 400, "step_up_failed");
    }

    [Fact]
    public async Task Step_up_accepts_an_ordinary_session_but_no_anonymous_or_api_key_caller()
    {
        var anonymous = vault.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/ui/step-up", new { code = "invalid" });
        await UiApiFailureContractTests.AssertProblemAsync(response, 401, "unauthenticated");
        var service = vault.CreateClient();
        service.DefaultRequestHeaders.Authorization = new("Bearer", await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-step-api-key"));
        await UiApiFailureContractTests.AssertProblemAsync(await service.PostAsJsonAsync("/api/ui/step-up", new { code = "invalid" }), 401, "unauthenticated");
        var owner = await vault.SignInNewAccountAsync("it-step-reader", IPAddress.Parse("198.18.109.22"));
        var account = await ProfileHttp.AccountAsync(vault, "it-step-reader");
        Assert.Equal(HttpStatusCode.NoContent, (await VerifyAsync(owner, Totp.Code(account.TotpSecret!, Totp.CurrentStep() + 1))).StatusCode);
    }

    [Fact]
    public async Task Step_up_spends_the_shared_source_sign_in_budget_before_testing_the_code()
    {
        var owner = await vault.SignInNewAccountAsync("it-step-throttle", IPAddress.Parse("198.18.109.23"));
        var session = vault.CreateSession(owner.SessionCookie, IPAddress.Parse("198.18.109.24"));
        var token = await UiApiRequest.AntiforgeryTokenAsync(session.Client);
        await ExhaustSourceBudgetAsync("198.18.109.24");

        var response = await UiApiRequest.SendWithTokenAsync(session.Client, token, HttpMethod.Post, "/api/ui/step-up", JsonContent.Create(new { code = "invalid" }));

        await UiApiFailureContractTests.AssertProblemAsync(response, 429, "throttled");
        Assert.Equal(0, (await ProfileHttp.AccountAsync(vault, "it-step-throttle")).FailedAccessCount);
    }

    private async Task ExhaustSourceBudgetAsync(string source)
    {
        var throttle = vault.Services.GetRequiredService<EclipsVault.Core.Application.Abstractions.IAuthThrottle>();
        var options = vault.Services.GetRequiredService<IOptions<EclipsVault.Infrastructure.Security.AuthThrottleOptions>>().Value;
        for (var attempt = 0; attempt < options.PermitLimit; attempt++)
        {
            Assert.True(await throttle.TryAcquireAsync(source, default));
        }
    }

    private static Task<HttpResponseMessage> VerifyAsync(VaultSession session, string code)
        => UiApiRequest.WriteWithAntiforgeryAsync(session, HttpMethod.Post, "/api/ui/step-up", JsonContent.Create(new { code }));
}
