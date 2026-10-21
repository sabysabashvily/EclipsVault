using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

internal static class LegacySharing
{
    public static async Task<string> CreateSecretAsync(HttpClient admin, string name)
    {
        var page = await admin.GetStringAsync("/Secrets/Create");
        var response = await admin.PostAsync("/Secrets/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = VaultFixture.FormTokenOf(page, "/Secrets/Create"),
            ["Name"] = name,
            ["Value"] = "it-share-audit-value",
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

    public static async Task<long> AuditHeadAsync(VaultFixture vault)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.AuditLogs.MaxAsync(row => row.Sequence);
    }

    public static async Task<AuditLog[]> ShareAuditRowsAfterAsync(VaultFixture vault, long sequence)
        => (await AuditRowsAfterAsync(vault, sequence))
            .Where(row => row.Action is AuditAction.SecretShared or AuditAction.SecretShareRevoked)
            .ToArray();

    public static async Task<AuditLog[]> AuditRowsAfterAsync(VaultFixture vault, long sequence)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.AuditLogs.AsNoTracking().Where(row => row.Sequence > sequence).OrderBy(row => row.Sequence).ToArrayAsync();
    }
}
