using System.Net;
using System.Net.Http.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A secret's page in the UI API, called as the Angular page calls it. Each write fetches the session's
/// antiforgery token first, from <c>/api/ui/me</c>, which records nothing.
/// </summary>
internal static class SecretDetailsApi
{
    /// <summary>The API path of the secret a details address (<c>/Secrets/Details/{id}</c>) names.</summary>
    public static string PathOf(string details) => $"/api/ui/secrets/{SecretSharing.SecretIdOf(details)}";

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string details) => client.GetAsync(PathOf(details));

    public static Task<HttpResponseMessage> RevealAsync(HttpClient client, string details)
        => SendAsync(client, HttpMethod.Post, $"{PathOf(details)}/reveal");

    public static Task<HttpResponseMessage> RevealVersionAsync(HttpClient client, string details, Guid versionId)
        => SendAsync(client, HttpMethod.Post, $"{PathOf(details)}/versions/{versionId}/reveal");

    public static Task<HttpResponseMessage> RotateAsync(HttpClient client, string details, string newValue, int? renewTtlDays = null)
        => SendAsync(client, HttpMethod.Post, $"{PathOf(details)}/rotate", JsonContent.Create(new { newValue, renewTtlDays }));

    public static Task<HttpResponseMessage> RotateUpstreamAsync(HttpClient client, string details)
        => SendAsync(client, HttpMethod.Post, $"{PathOf(details)}/rotate-managed", JsonContent.Create(new { }));

    public static Task<HttpResponseMessage> RestoreAsync(HttpClient client, string details, Guid versionId)
        => SendAsync(client, HttpMethod.Post, $"{PathOf(details)}/versions/{versionId}/restore");

    public static Task<HttpResponseMessage> ShareAsync(HttpClient issuer, string details, string recipient, int ttlDays = 0)
        => SendAsync(issuer, HttpMethod.Post, $"{PathOf(details)}/shares", JsonContent.Create(new { recipient, ttlDays }));

    public static Task<HttpResponseMessage> RevokeAsync(HttpClient issuer, string details, Guid shareId)
        => SendAsync(issuer, HttpMethod.Delete, $"{PathOf(details)}/shares/{shareId}");

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string details)
        => SendAsync(client, HttpMethod.Delete, PathOf(details));

    public static async Task<bool> ExistsAsync(VaultFixture vault, string details)
    {
        using var scope = vault.Services.CreateScope();
        var secretId = SecretSharing.SecretIdOf(details);
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.AnyAsync(secret => secret.Id == secretId);
    }

    public static async Task<int> VersionCountAsync(VaultFixture vault, string details)
    {
        using var scope = vault.Services.CreateScope();
        var secretId = SecretSharing.SecretIdOf(details);
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SecretVersions.CountAsync(version => version.SecretId == secretId);
    }

    /// <summary>The newest archived version, the one a restore in these tests puts back.</summary>
    public static async Task<Guid> NewestVersionAsync(VaultFixture vault, string details)
    {
        using var scope = vault.Services.CreateScope();
        var secretId = SecretSharing.SecretIdOf(details);
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SecretVersions.AsNoTracking()
            .Where(version => version.SecretId == secretId)
            .OrderByDescending(version => version.VersionNumber).Select(version => version.Id).FirstAsync();
    }

    /// <summary>Binds a secret to a backend login the way an operator does, in the database: no screen makes one.</summary>
    public static async Task MakeManagedAsync(VaultFixture vault, string details, string login)
    {
        using var scope = vault.Services.CreateScope();
        var secretId = SecretSharing.SecretIdOf(details);
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.Where(secret => secret.Id == secretId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(secret => secret.RotationBackend, DynamicSecretBackend.SqlServer)
                .SetProperty(secret => secret.RotationPrincipal, login));
    }

    /// <summary>The audit actions written for one secret after <paramref name="sequence"/>, oldest first.</summary>
    public static async Task<AuditAction[]> ActionsAfterAsync(VaultFixture vault, long sequence, string details)
        => [.. (await AuditTrail.RowsAfterAsync(vault, sequence))
            .Where(row => row.ResourceId == SecretSharing.SecretIdOf(details))
            .Select(row => row.Action)];

    public static void AssertNoContent(HttpResponseMessage response) => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, HttpContent? content = null)
        => await UiApiRequest.SendWithTokenAsync(client, await UiApiRequest.AntiforgeryTokenAsync(client), method, path, content);
}
