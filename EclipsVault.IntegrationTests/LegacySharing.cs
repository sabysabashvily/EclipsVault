using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

internal static class LegacySharing
{
    public static async Task<string> CreateSecretAsync(HttpClient admin, string name, string value = "it-share-audit-value")
    {
        var page = await admin.GetStringAsync("/Secrets/Create");
        var response = await admin.PostAsync("/Secrets/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(page, "/Secrets/Create"),
            ["Name"] = name,
            ["Value"] = value,
            ["ProjectKey"] = "GLOBAL",
            ["Environment"] = "Development",
            ["Sensitivity"] = "Internal",
        }));
        return response.Headers.Location?.OriginalString is { } details && details.StartsWith("/Secrets/Details/", StringComparison.Ordinal)
            ? details
            : throw new InvalidOperationException($"Creating {name} answered {(int)response.StatusCode}, not secret details.");
    }

    public static Guid SecretIdOf(string details) => Guid.Parse(details["/Secrets/Details/".Length..]);

    public static async Task<HttpResponseMessage> ShareAsync(HttpClient issuer, string details, string recipient, string ttlDays = "0")
    {
        var page = await issuer.GetStringAsync(details);
        return await issuer.PostAsync("/Secrets/Grant", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(page, details),
            ["SecretId"] = SecretIdOf(details).ToString(),
            ["GranteeUsernameOrEmail"] = recipient,
            ["TtlDays"] = ttlDays,
        }));
    }

    public static async Task<HttpResponseMessage> RevokeAsync(HttpClient issuer, string details, Guid shareId)
    {
        var page = await issuer.GetStringAsync(details);
        return await issuer.PostAsync("/Secrets/RevokeGrant", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(page, details),
            ["id"] = SecretIdOf(details).ToString(),
            ["grantId"] = shareId.ToString(),
        }));
    }

    public static async Task<SecretGrant> ShareOfAsync(VaultFixture vault, string details)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var secretId = SecretIdOf(details);
        return await db.SecretGrants.AsNoTracking().SingleAsync(share => share.SecretId == secretId);
    }
}
