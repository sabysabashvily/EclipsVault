using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

internal static class LegacyAccessRequests
{
    // Antiforgery tokens belong to the session, not to one form, and every signed-in account can open
    // this page, so it serves the token for the requester's and the reviewer's posts alike.
    public static async Task<string> TokenAsync(HttpClient client)
        => VaultFixture.FormTokenOf(await client.GetStringAsync("/Secrets/Create"), "/Secrets/Create");

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string action, string token, Guid id, string ttlDays = "0")
        => PostFormAsync(client, action, new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            // Create names the secret; the decisions name the request.
            ["id"] = id.ToString(),
            ["secretId"] = id.ToString(),
            ["reason"] = "Need this for deployment",
            ["ttlDays"] = ttlDays,
        });

    public static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string action, Dictionary<string, string> form)
        => client.PostAsync($"/AccessRequests/{action}", new FormUrlEncodedContent(form));

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
