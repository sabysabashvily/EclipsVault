using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

/// <summary>Verified Profile requests and their persisted results, using test-owned credentials.</summary>
internal static class ProfileHttp
{
    /// <summary>A complete 1×1 PNG, the smallest image the avatar upload accepts.</summary>
    public static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static ProfileAccount Owner(VaultSession browser)
        => browser.ProfileOwner ?? throw new InvalidOperationException("Use a test-owned signed-in account");

    public static Task<HttpResponseMessage> UpdateDisplayNameAsync(VaultSession browser, string displayName)
        => ProfileApi.SendAsync(browser, HttpMethod.Patch, "/api/ui/profile/details", new { displayName });

    public static async Task<HttpResponseMessage> ChangePasswordAsync(VaultSession browser, string currentPassword, string newPassword)
    {
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(Owner(browser) with { Password = currentPassword },
            new { kind = "ChangePassword", newPassword });
        return await ProfileApi.SendAsync(browser, HttpMethod.Post, "/api/ui/profile/password", new { operationId });
    }

    public static async Task<HttpResponseMessage> UploadAvatarAsync(VaultSession browser, byte[] image)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(image), "avatar", "avatar.png" } };
        return await UiApiRequest.WriteWithAntiforgeryAsync(browser, HttpMethod.Post, "/api/ui/profile/avatar", form);
    }

    public static Task<HttpResponseMessage> RemoveAvatarAsync(VaultSession browser)
        => ProfileApi.SendAsync(browser, HttpMethod.Delete, "/api/ui/profile/avatar");

    public static async Task<HttpResponseMessage> ResetAuthenticatorAsync(VaultSession browser)
    {
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(Owner(browser), new { kind = "ResetAuthenticator" });
        return await ProfileApi.SendAsync(browser, HttpMethod.Post, "/api/ui/profile/authenticator/reset", new { operationId });
    }

    public static async Task<HttpResponseMessage> GenerateRecoveryCodesAsync(VaultSession browser)
    {
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(Owner(browser), new { kind = "ReplaceRecoveryCodes" });
        return await ProfileApi.SendAsync(browser, HttpMethod.Post, "/api/ui/profile/recovery-codes", new { operationId });
    }

    public static Task<HttpResponseMessage> SignOutEverywhereAsync(VaultSession browser)
        => ProfileApi.SendAsync(browser, HttpMethod.Post, "/api/ui/profile/sign-out-everywhere");

    public static async Task<HttpResponseMessage> RemovePasskeyAsync(VaultSession browser, Guid passkeyId)
    {
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(Owner(browser), new { kind = "RemovePasskey", passkeyId });
        return await ProfileApi.SendAsync(browser, HttpMethod.Delete, $"/api/ui/profile/passkeys/{passkeyId}", new { operationId });
    }

    public static async Task<HttpResponseMessage> RegisterPasskeyAsync(VaultSession browser, PasskeyAuthenticator authenticator, string nickname)
    {
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(Owner(browser), new { kind = "AddPasskey", passkeyNickname = nickname });
        var options = await ProfileApi.PasskeyPromptAsync(browser, "/api/ui/profile/passkeys/registration/begin", new { operationId });
        return await ProfileApi.SendAsync(browser, HttpMethod.Post, "/api/ui/profile/passkeys/registration/complete",
            new { operationId, credential = authenticator.Register(options) });
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

}
