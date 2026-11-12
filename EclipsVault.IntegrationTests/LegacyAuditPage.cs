namespace EclipsVault.IntegrationTests;

internal static class LegacyAuditPage
{
    /// <summary>Posts one of the Legacy Audit page's buttons, which send nothing but the form token.</summary>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string token)
        => client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
}
