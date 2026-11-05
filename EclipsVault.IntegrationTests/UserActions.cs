namespace EclipsVault.IntegrationTests;

internal static class UserActions
{
    public static Task<HttpResponseMessage> SetEnabledAsync(HttpClient admin, Guid accountId, bool enabled)
        => SendAsync(admin, HttpMethod.Post, $"/api/ui/users/{accountId}/{(enabled ? "enable" : "disable")}");

    public static Task<HttpResponseMessage> UnlockAsync(HttpClient admin, Guid accountId)
        => SendAsync(admin, HttpMethod.Post, $"/api/ui/users/{accountId}/unlock");

    public static Task<HttpResponseMessage> ForceLogoutAsync(HttpClient admin, Guid accountId)
        => SendAsync(admin, HttpMethod.Post, $"/api/ui/users/{accountId}/sign-out");

    public static Task<HttpResponseMessage> ResetMfaAsync(HttpClient admin, Guid accountId)
        => SendAsync(admin, HttpMethod.Post, $"/api/ui/users/{accountId}/reset-mfa");

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient admin, Guid accountId)
        => SendAsync(admin, HttpMethod.Delete, $"/api/ui/users/{accountId}");

    private static async Task<HttpResponseMessage> SendAsync(HttpClient admin, HttpMethod method, string path)
        => await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), method, path);
}
