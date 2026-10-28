using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>A signed-in account whose credentials the test knows, as its owner would.</summary>
internal sealed record ProfileAccount(VaultSession Browser, User Stored, string Password)
{
    public Guid Id => Stored.Id;

    public string TotpSecret => Stored.TotpSecret!;

    /// <summary>
    /// A new account signed in through its real first sign-in, which spends two of its address's
    /// eleven sign-in requests. Give every test its own <paramref name="address"/>.
    /// </summary>
    public static async Task<ProfileAccount> CreateAsync(VaultFixture vault, string username, string address)
    {
        var password = "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var browser = await vault.SignInNewAccountAsync(username, IPAddress.Parse(address), password: password);
        return new ProfileAccount(browser, await LegacyProfile.AccountAsync(vault, username), password);
    }
}

/// <summary>The Profile UI API as the Angular UI calls it: JSON bodies with the session's antiforgery token.</summary>
internal static class ProfileApi
{
    public static async Task AssertNoticeAsync(VaultFixture vault, ProfileAccount account, string eventType, int expectedCount = 1)
    {
        using var scope = vault.Services.CreateScope();
        var notices = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().EmailLogs.AsNoTracking()
            .Where(email => email.ToAddress == account.Stored.Email && email.EventType == eventType).ToArrayAsync();
        Assert.Equal(expectedCount, notices.Length);
        Assert.All(notices, notice =>
        {
            Assert.Equal(EclipsVault.Core.Domain.Enums.EmailDeliveryStatus.Sent, notice.Status);
            Assert.Contains("If this wasn't you", notice.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(account.Password, notice.Body, StringComparison.Ordinal);
        });
    }

    public static async Task<string[]> RecoverySetAsync(ProfileAccount account)
    {
        Assert.Equal(HttpStatusCode.Redirect, (await LegacyProfile.GenerateRecoveryCodesAsync(account.Browser.Client)).StatusCode);
        var page = await account.Browser.Client.GetStringAsync("/Profile/RecoveryCodes");
        var codes = System.Text.RegularExpressions.Regex.Matches(page, "<li class=\"mono\">([^<]+)</li>")
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)).ToArray();
        Assert.Equal(10, codes.Length);
        return codes;
    }

    public static async Task<ProfileOperation> OperationAsync(VaultFixture vault, Guid id)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().ProfileOperations.AsNoTracking().SingleAsync(o => o.Id == id);
    }

    public static async Task ExpireAsync(VaultFixture vault, Guid id)
    {
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().ProfileOperations.Where(o => o.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(o => o.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    public static async Task ExpireGrantAsync(VaultFixture vault, Guid id)
    {
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().ProfileOperations.Where(o => o.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(o => o.VerifiedAtUtc, DateTimeOffset.UtcNow.AddMinutes(-6)));
    }

    public static async Task<VaultSession> SecondSessionAsync(VaultFixture vault, ProfileAccount owner, string address)
    {
        var session = vault.CreateSession(source: IPAddress.Parse(address));
        var login = await session.Client.GetStringAsync("/Account/Login");
        var password = await session.Client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(login, "/Account/Login"),
            ["Username"] = owner.Stored.Username,
            ["Password"] = owner.Password,
        }));
        Assert.Equal("/Account/Totp", password.Headers.Location?.OriginalString);
        var page = await session.Client.GetStringAsync("/Account/Totp");
        var verified = await session.Client.PostAsync("/Account/Totp", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(page, "/Account/Totp"),
            ["Code"] = Totp.NextCode(owner.TotpSecret),
        }));
        Assert.Equal(HttpStatusCode.Redirect, verified.StatusCode);
        Assert.NotNull(session.SessionCookie);
        return session;
    }

    private static readonly string[] ProblemMembers = ["code", "detail", "errors", "status", "title", "type"];

    public static Task<HttpResponseMessage> SendAsync(VaultSession browser, HttpMethod method, string path, object? body = null)
        => UiApiRequest.WriteWithAntiforgeryAsync(browser, method, path, body is null ? null : JsonContent.Create(body));

    public static Task<HttpResponseMessage> PrepareAsync(VaultSession browser, object change)
        => SendAsync(browser, HttpMethod.Post, "/api/ui/profile/operations", change);

    /// <summary>Prepares a change that must succeed and returns its operation id.</summary>
    public static async Task<Guid> PreparedAsync(VaultSession browser, object change)
    {
        var response = await PrepareAsync(browser, change);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationId").GetGuid();
    }

    public static Task<HttpResponseMessage> VerifyAsync(VaultSession browser, Guid operationId, string method, string currentPassword, string code)
        => SendAsync(browser, HttpMethod.Post, $"/api/ui/profile/operations/{operationId}/verify",
            new { method, currentPassword, code });

    /// <summary>Prepares a change and verifies it with the account's password and its next authenticator code.</summary>
    public static async Task<Guid> VerifiedWithAuthenticatorAsync(ProfileAccount account, object change)
    {
        var operationId = await PreparedAsync(account.Browser, change);
        var verified = await VerifyAsync(account.Browser, operationId, "PasswordAndAuthenticatorCode", account.Password,
            Totp.NextCode(account.TotpSecret));
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        return operationId;
    }

    public static async Task<JsonElement> PasskeyPromptAsync(VaultSession browser, string path, object? body = null)
    {
        var response = await SendAsync(browser, HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("publicKey");
    }

    public static Task<HttpResponseMessage> CompletePasskeyAsync(VaultSession browser, Guid operationId, object credential, string? currentPassword = null)
        => SendAsync(browser, HttpMethod.Post, $"/api/ui/profile/operations/{operationId}/passkey/complete",
            new { credential, currentPassword });

    /// <summary>Adds a passkey through the Profile API, verified with the account's next authenticator code.</summary>
    public static async Task<Guid> AddPasskeyAsync(ProfileAccount account, PasskeyAuthenticator authenticator, string nickname)
    {
        var operationId = await VerifiedWithAuthenticatorAsync(account, new { kind = "AddPasskey", passkeyNickname = nickname });
        var options = await PasskeyPromptAsync(account.Browser, "/api/ui/profile/passkeys/registration/begin", new { operationId });
        var registered = await SendAsync(account.Browser, HttpMethod.Post, "/api/ui/profile/passkeys/registration/complete",
            new { operationId, credential = authenticator.Register(options) });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        return (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>A Profile Problem Details answer: its status, code, no-store, and only problem members.</summary>
    public static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "The response is not no-store");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(problem.EnumerateObject(), member => Assert.Contains(member.Name, ProblemMembers));
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    public static string[] NamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}
