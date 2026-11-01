using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

internal static class LegacyAccessRequests
{
    // Antiforgery tokens belong to the session, not to one form, and every signed-in account can open
    // this page, so it serves the token for the Denied page's form post.
    public static async Task<string> TokenAsync(HttpClient client)
        => VaultFixture.FormTokenOf(await client.GetStringAsync("/Secrets/Create"), "/Secrets/Create");

    /// <summary>Files a request the way the Denied page's form does.</summary>
    public static Task<HttpResponseMessage> FileAsync(HttpClient client, string token, Guid secretId, string reason = "Need this for deployment")
        => PostFormAsync(client, "Create", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["secretId"] = secretId.ToString(),
            ["reason"] = reason,
        });

    public static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string action, Dictionary<string, string> form)
        => client.PostAsync($"/AccessRequests/{action}", new FormUrlEncodedContent(form));

    /// <summary>
    /// Inserts a pending request straight into the database, skipping filing and its rule, for tests
    /// about deciding a request rather than filing one. The row has no audit entry and no actor.
    /// </summary>
    public static async Task<AccessRequest> PendingAsync(VaultFixture vault, string details, string username)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var user = await db.Users.SingleAsync(user => user.Username == username);
        var secretId = LegacySharing.SecretIdOf(details);
        var secret = await db.Secrets.SingleAsync(secret => secret.Id == secretId);
        var request = new AccessRequest
        {
            Id = Guid.NewGuid(),
            SecretId = secretId,
            SecretName = secret.Name,
            ProjectKey = secret.ProjectKey,
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
