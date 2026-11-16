using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>The buttons on the Legacy secret details page, pressed as a browser presses them.</summary>
internal static class LegacySecretActions
{
    /// <summary>The details page's form token, read once: opening the page records a view of its own.</summary>
    public static async Task<string> FormTokenAsync(HttpClient client, string details)
        => VaultFixture.FormTokenOf(await client.GetStringAsync(details), details);

    public static Task<HttpResponseMessage> RevealAsync(HttpClient client, string details, string token)
        => PostAsync(client, details.Replace("/Details/", "/Reveal/", StringComparison.Ordinal), token, []);

    public static Task<HttpResponseMessage> RotateAsync(HttpClient client, string details, string token, string newValue)
        => PostAsync(client, "/Secrets/Rotate", token, new()
        {
            ["Id"] = LegacySharing.SecretIdOf(details).ToString(),
            ["NewValue"] = newValue,
        });

    public static Task<HttpResponseMessage> RotateUpstreamAsync(HttpClient client, string details, string token)
        => PostAsync(client, details.Replace("/Details/", "/RotateManaged/", StringComparison.Ordinal), token, []);

    public static Task<HttpResponseMessage> RestoreAsync(HttpClient client, string details, string token, Guid versionId)
        => PostAsync(client, details.Replace("/Details/", "/RestoreVersion/", StringComparison.Ordinal), token, new()
        {
            ["versionId"] = versionId.ToString(),
        });

    /// <summary>The newest archived version, the one a restore in these tests puts back.</summary>
    public static async Task<Guid> NewestVersionAsync(VaultFixture vault, string details)
    {
        using var scope = vault.Services.CreateScope();
        var secretId = LegacySharing.SecretIdOf(details);
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SecretVersions.AsNoTracking()
            .Where(version => version.SecretId == secretId)
            .OrderByDescending(version => version.VersionNumber).Select(version => version.Id).FirstAsync();
    }

    /// <summary>Binds a secret to a backend login the way an operator does, in the database: no screen makes one.</summary>
    public static async Task MakeManagedAsync(VaultFixture vault, string details, string login)
    {
        using var scope = vault.Services.CreateScope();
        var secretId = LegacySharing.SecretIdOf(details);
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.Where(secret => secret.Id == secretId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(secret => secret.RotationBackend, DynamicSecretBackend.SqlServer)
                .SetProperty(secret => secret.RotationPrincipal, login));
    }

    /// <summary>The audit actions written for one secret after <paramref name="sequence"/>, oldest first.</summary>
    public static async Task<AuditAction[]> ActionsAfterAsync(VaultFixture vault, long sequence, string details)
        => [.. (await AuditTrail.RowsAfterAsync(vault, sequence))
            .Where(row => row.ResourceId == LegacySharing.SecretIdOf(details))
            .Select(row => row.Action)];

    public static void AssertRedirectedTo(HttpResponseMessage response, string location)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(location, response.Headers.Location?.OriginalString);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string token, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = token;
        return client.PostAsync(path, new FormUrlEncodedContent(fields));
    }
}
