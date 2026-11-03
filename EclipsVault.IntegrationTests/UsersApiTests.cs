using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Models.Ui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class UsersApiTests(VaultFixture vault)
{
    private const string Missing = "/api/ui/users/11111111-1111-1111-1111-111111111111";

    [Theory]
    [InlineData("GET", "/api/ui/users", "198.18.110.101")]
    [InlineData("GET", "/api/ui/users/notice", "198.18.110.102")]
    [InlineData("POST", Missing + "/enable", "198.18.110.103")]
    [InlineData("POST", Missing + "/disable", "198.18.110.104")]
    [InlineData("POST", Missing + "/unlock", "198.18.110.105")]
    [InlineData("POST", Missing + "/reset-mfa", "198.18.110.106")]
    [InlineData("POST", Missing + "/sign-out", "198.18.110.107")]
    [InlineData("DELETE", Missing, "198.18.110.108")]
    public async Task Every_users_endpoint_requires_an_administrator_session(string method, string path, string source)
    {
        var anonymous = vault.CreateClient();
        var reader = await vault.SignInNewAccountAsync("it-users-reader-" + method + path.Split('/').Last(), IPAddress.Parse(source));
        var keyClient = vault.CreateClient();
        keyClient.DefaultRequestHeaders.Authorization = new("Bearer", await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-users-key-" + Guid.NewGuid().ToString("N")));

        await UiApiFailureContractTests.AssertProblemAsync(await anonymous.SendAsync(new(new HttpMethod(method), path)), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(await reader.Client.SendAsync(new(new HttpMethod(method), path)), 403, "forbidden");
        await UiApiFailureContractTests.AssertProblemAsync(await keyClient.SendAsync(new(new HttpMethod(method), path)), 401, "unauthenticated");
    }


    [Theory]
    [InlineData("POST", Missing + "/enable")]
    [InlineData("POST", Missing + "/disable")]
    [InlineData("POST", Missing + "/unlock")]
    [InlineData("POST", Missing + "/reset-mfa")]
    [InlineData("POST", Missing + "/sign-out")]
    [InlineData("DELETE", Missing)]
    [InlineData("POST", "/api/ui/step-up")]
    public async Task Writes_without_antiforgery_are_refused_before_the_action(string method, string path)
    {
        var admin = await vault.AdminClientAsync();
        var response = await admin.SendAsync(new(new HttpMethod(method), path) { Content = JsonContent.Create(new { code = "000000" }) });
        await UiApiFailureContractTests.AssertProblemAsync(response, 400, "antiforgery_rejected");
    }

    [Theory]
    [InlineData("POST", Missing + "/enable")]
    [InlineData("POST", Missing + "/disable")]
    [InlineData("POST", Missing + "/unlock")]
    [InlineData("POST", Missing + "/reset-mfa")]
    [InlineData("POST", Missing + "/sign-out")]
    [InlineData("DELETE", Missing)]
    public async Task A_fresh_administrator_gets_the_missing_user_problem(string method, string path)
    {
        var admin = await vault.AdminClientAsync();
        var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), new(method), path);
        await UiApiFailureContractTests.AssertProblemAsync(response, 404, "user_not_found");
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("unlock")]
    [InlineData("reset-mfa")]
    public async Task Stale_grants_require_proof_before_looking_up_a_missing_target(string action)
    {
        var admin = await vault.SignInNewAccountAsync("it-users-stale-" + action, IPAddress.Parse("198.18.109.11"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.109.11"));
        var response = await UiApiRequest.WriteWithAntiforgeryAsync(stale, HttpMethod.Post, Missing + "/" + action);
        await AdministratorFreshnessTests.AssertStepUpAsync(response, "code");
    }

    [Theory]
    [InlineData("POST", Missing + "/disable")]
    [InlineData("POST", Missing + "/sign-out")]
    [InlineData("DELETE", Missing)]
    public async Task Removing_access_does_not_demand_fresh_proof(string method, string path)
    {
        var admin = await vault.SignInNewAccountAsync("it-users-remove-" + method + path.Split('/').Last(), IPAddress.Parse($"198.18.112.{path.Length}"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddDays(-1)), IPAddress.Parse("198.18.109.15"));
        var response = await UiApiRequest.WriteWithAntiforgeryAsync(stale, new(method), path);
        await UiApiFailureContractTests.AssertProblemAsync(response, 404, "user_not_found");
    }

    [Fact]
    public async Task A_stale_account_without_an_authenticator_can_end_its_session_and_reach_sign_in()
    {
        var admin = await vault.SignInNewAccountAsync("it-users-no-factor", IPAddress.Parse("198.18.109.12"), ClearanceLevel.TopSecret);
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.Users.Where(user => user.Username == "it-users-no-factor").ExecuteUpdateAsync(change => change.SetProperty(user => user.TotpEnabled, false));
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.109.12"));
        await AdministratorFreshnessTests.AssertStepUpAsync(await UserActions.UnlockAsync(stale.Client, Guid.NewGuid()), "sign_in");
        var loginWhileAuthenticated = await stale.Client.GetAsync("/Account/Login?ReturnUrl=%2FAdmin%2FUsers");
        Assert.Equal(HttpStatusCode.Redirect, loginWhileAuthenticated.StatusCode);
        Assert.Equal("/Admin/Users", loginWhileAuthenticated.Headers.Location?.OriginalString);

        var signedOut = await UiApiRequest.WriteWithAntiforgeryAsync(stale, HttpMethod.Post, "/Account/Logout");
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Null(stale.SessionCookie);
        var login = await stale.Client.GetAsync("/Account/Login?ReturnUrl=%2FAdmin%2FUsers");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("/Admin/Users", await login.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("last-administrator", "last_administrator")]
    [InlineData("changed", "account_changed")]
    public async Task Concurrent_administrator_conflicts_have_the_agreed_problem_codes(string kind, string code)
    {
        var admin = await vault.AdminClientAsync();
        await UiApiFailureContractTests.AssertProblemAsync(await admin.GetAsync("/api/ui/test-only/administrator-conflict/" + kind), 409, code);
    }

    [Fact]
    public async Task The_list_has_only_the_agreed_fields_and_identifies_the_caller()
    {
        var admin = await vault.SignInNewAccountAsync("it-users-shape", IPAddress.Parse("198.18.109.13"), ClearanceLevel.TopSecret);
        var response = await admin.Client.GetAsync("/api/ui/users");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["users"], body.RootElement.EnumerateObject().Select(member => member.Name));
        var caller = Assert.Single(body.RootElement.GetProperty("users").EnumerateArray(), user => user.GetProperty("isYou").GetBoolean());
        Assert.Equal(["avatarUrl", "clearance", "clearanceLabel", "displayName", "email", "isYou", "mfaEnrolled", "project", "status", "userId", "username"], caller.EnumerateObject().Select(member => member.Name).Order());
        Assert.Equal("it-users-shape", caller.GetProperty("username").GetString());
        Assert.Equal("active", caller.GetProperty("status").GetString());
        Assert.True(caller.GetProperty("mfaEnrolled").GetBoolean());
        Assert.Equal(4, caller.GetProperty("clearance").GetInt32());
    }

    [Fact]
    public async Task Legacy_create_notices_are_returned_once_separately_from_the_list()
    {
        var admin = await vault.AdminClientAsync();
        await LegacyUsers.CreateAsync(admin, "it-users-notice");
        var account = await ProfileHttp.AccountAsync(vault, "it-users-notice");
        var notice = await admin.GetFromJsonAsync<UserNoticeResponse>("/api/ui/users/notice");
        Assert.Equal(new UserNoticeResponse($"User 'it-users-notice' created with email {account.Email}. They can sign in with either and will set up their authenticator on first sign-in.", false), notice);
        Assert.Equal(new UserNoticeResponse(null, false), await admin.GetFromJsonAsync<UserNoticeResponse>("/api/ui/users/notice"));
    }

    [Fact]
    public async Task A_non_administrator_visiting_Users_is_sent_to_Denied()
    {
        var reader = await vault.SignInNewAccountAsync("it-users-denied", IPAddress.Parse("198.18.109.14"));
        var response = await reader.Client.GetAsync("/Admin/Users");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Denied", response.Headers.Location?.PathAndQuery);
    }
}
