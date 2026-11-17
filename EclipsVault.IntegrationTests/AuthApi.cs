using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>The sign-in UI API, <c>/api/ui/auth</c>, driven as the sign-in pages drive it (ADR-0032).</summary>
internal static class AuthApi
{
    /// <summary>
    /// The anonymous request token a sign-in page carries. Until the Angular sign-in pages are served,
    /// it is the Legacy form's; the Recover page is the one a blocked network can load.
    /// </summary>
    public static async Task<string> AnonymousTokenAsync(VaultSession session, string page = "/Account/Login")
        => VaultFixture.FormTokenOf(await session.Client.GetStringAsync(page), page);

    /// <summary>The request token the last change of identity reissued, as the Angular UI reads it.</summary>
    public static string ReissuedToken(VaultSession session)
        => Assert.IsType<Cookie>(session.Cookies.GetCookies(session.Client.BaseAddress!)["XSRF-TOKEN"]).Value;

    public static Task<HttpResponseMessage> PostAsync(VaultSession session, string path, string token, object? body = null)
        => UiApiRequest.SendWithTokenAsync(session.Client, token, HttpMethod.Post, path, body is null ? null : JsonContent.Create(body));

    /// <summary>The password stage from an anonymous page; on success the session holds a pending sign-in.</summary>
    public static async Task<HttpResponseMessage> PasswordAsync(VaultSession session, string username, string password, string? returnUrl = null)
        => await PostAsync(session, "/api/ui/auth/password", await AnonymousTokenAsync(session), new { username, password, returnUrl });

    /// <summary>The password stage, which must pass, leaving the token the second factor needs.</summary>
    public static async Task<string> PendingAsync(VaultSession session, string username, string password, string next = "totp")
    {
        var response = await PasswordAsync(session, username, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(next, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("next").GetString());
        return ReissuedToken(session);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The account's authenticator secret, as stored.</summary>
    public static async Task<string> TotpSecretAsync(VaultFixture vault, string username)
        => (await ProfileHttp.AccountAsync(vault, username)).TotpSecret!;

    /// <summary>
    /// Sets the account's generation forward inside <paramref name="held"/>'s transaction, so the change
    /// commits when the holder does, before any writer queued behind the audit chain.
    /// </summary>
    public static Task AdvanceCredentialGenerationAsync(HeldAuditChain held, Guid accountId)
        => held.Services.GetRequiredService<EclipsVaultDbContext>().Users.Where(user => user.Id == accountId)
            .ExecuteUpdateAsync(set => set.SetProperty(user => user.CredentialGeneration, user => user.CredentialGeneration + 1));

    /// <summary>
    /// Lets <paramref name="writers"/> audited commits of one request through in turn, then holds the
    /// chain against the next and returns the holder. That writer waits until the holder is released.
    /// </summary>
    public static async Task<HeldAuditChain> HoldAfterWritersAsync(VaultFixture vault, HeldAuditChain held, int writers)
    {
        for (var i = 0; i < writers; i++)
        {
            held = await held.HandOverAfterNextWriterAsync(vault);
        }

        await held.WaitForQueuedWritersAsync(1);
        return held;
    }
}
