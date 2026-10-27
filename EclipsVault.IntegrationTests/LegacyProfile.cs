using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

/// <summary>The Legacy Profile page's own forms, posted as a browser posts them, and what they stored.</summary>
internal static class LegacyProfile
{
    /// <summary>A complete 1×1 PNG, the smallest image the avatar upload accepts.</summary>
    public static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public static Task<HttpResponseMessage> UpdateDetailsAsync(HttpClient client, string displayName, string email)
        => PostFromProfileAsync(client, "/Profile/Update", new() { ["DisplayName"] = displayName, ["Email"] = email });

    public static async Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string currentPassword, string newPassword)
    {
        var page = await client.GetStringAsync("/Profile/ChangePassword");
        return await client.PostAsync("/Profile/ChangePassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(page, "/Profile/ChangePassword"),
            ["CurrentPassword"] = currentPassword,
            ["NewPassword"] = newPassword,
            ["ConfirmPassword"] = newPassword,
        }));
    }

    public static async Task<HttpResponseMessage> UploadAvatarAsync(HttpClient client, byte[] image)
    {
        var page = await client.GetStringAsync("/Profile");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(VaultFixture.FormTokenOf(page, "/Profile")), "__RequestVerificationToken" },
            { new ByteArrayContent(image), "avatar", "avatar.png" },
        };
        return await client.PostAsync("/Profile/UploadAvatar", form);
    }

    public static Task<HttpResponseMessage> RemoveAvatarAsync(HttpClient client)
        => PostFromProfileAsync(client, "/Profile/RemoveAvatar", []);

    public static Task<HttpResponseMessage> ResetAuthenticatorAsync(HttpClient client)
        => PostFromProfileAsync(client, "/Profile/ResetMfa", []);

    public static Task<HttpResponseMessage> GenerateRecoveryCodesAsync(HttpClient client)
        => PostFromProfileAsync(client, "/Profile/GenerateRecoveryCodes", []);

    public static Task<HttpResponseMessage> SignOutEverywhereAsync(HttpClient client)
        => PostFromProfileAsync(client, "/Profile/SignOutEverywhere", []);

    public static Task<HttpResponseMessage> RemovePasskeyAsync(HttpClient client, Guid passkeyId)
        => PostFromProfileAsync(client, "/Profile/RemovePasskey", new() { ["id"] = passkeyId.ToString() });

    public static async Task<HttpResponseMessage> RegisterPasskeyAsync(HttpClient client, PasskeyAuthenticator authenticator, string nickname)
    {
        var page = await client.GetStringAsync("/Profile");
        using var begin = WithPageToken(page, "/Profile/PasskeyRegisterBegin", new { });
        var options = await (await client.SendAsync(begin)).Content.ReadFromJsonAsync<JsonElement>();
        using var complete = WithPageToken(page, "/Profile/PasskeyRegisterComplete", new
        {
            credential = authenticator.Register(options),
            nickname,
        });
        return await client.SendAsync(complete);
    }

    public static async Task<User> AccountAsync(VaultFixture vault, string username)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(user => user.Username == username);
    }

    public static async Task<MfaRecoveryCode[]> RecoveryCodesAsync(VaultFixture vault, Guid accountId)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.MfaRecoveryCodes.AsNoTracking().Where(code => code.UserId == accountId).OrderBy(code => code.Id).ToArrayAsync();
    }

    public static async Task<PasskeyCredential[]> PasskeysAsync(VaultFixture vault, Guid accountId)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.PasskeyCredentials.AsNoTracking().Where(passkey => passkey.UserId == accountId).ToArrayAsync();
    }

    public static async Task<bool> HasAvatarAsync(VaultFixture vault, Guid accountId)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.UserAvatars.AnyAsync(avatar => avatar.UserId == accountId);
    }

    /// <summary>The events recorded about one account after <paramref name="sequence"/>, oldest first.</summary>
    public static async Task<AuditAction[]> EventsAfterAsync(VaultFixture vault, long sequence, Guid accountId)
        => (await AuditTrail.RowsAfterAsync(vault, sequence))
            .Where(row => row.ResourceId == accountId)
            .Select(row => row.Action)
            .ToArray();

    private static async Task<HttpResponseMessage> PostFromProfileAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = VaultFixture.FormTokenOf(await client.GetStringAsync("/Profile"), "/Profile");
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static HttpRequestMessage WithPageToken(string page, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", VaultFixture.FormTokenOf(page, "/Profile"));
        return request;
    }
}
