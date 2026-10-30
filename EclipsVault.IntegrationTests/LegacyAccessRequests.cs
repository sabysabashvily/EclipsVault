using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

internal static class LegacyAccessRequests
{
    public static async Task<string> TokenAsync(HttpClient client)
        => VaultFixture.FormTokenOf(await client.GetStringAsync("/Secrets/Create"), "/Secrets/Create");

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string action, string token, Guid id, string ttlDays = "0")
        => client.PostAsync($"/AccessRequests/{action}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = id.ToString(),
            ["secretId"] = id.ToString(),
            ["reason"] = "Need this for deployment",
            ["ttlDays"] = ttlDays,
            ["note"] = "A forged decision note",
        }));

    public static async Task<AccessRequest> PendingAsync(VaultFixture vault, string details, string username)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var user = await db.Users.SingleAsync(user => user.Username == username);
        var request = new AccessRequest
        {
            Id = Guid.NewGuid(),
            SecretId = LegacySharing.SecretIdOf(details),
            SecretName = (await db.Secrets.SingleAsync(secret => secret.Id == LegacySharing.SecretIdOf(details))).Name,
            ProjectKey = "GLOBAL",
            RequesterUserId = user.Id,
            RequesterUsername = username,
            Reason = "Need this for deployment",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.AccessRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }
}
