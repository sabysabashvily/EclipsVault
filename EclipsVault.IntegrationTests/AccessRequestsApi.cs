using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

/// <summary>The Access requests page's UI API, called as the Angular UI calls it.</summary>
internal static class AccessRequestsApi
{
    public const string Path = "/api/ui/access-requests";

    public static async Task<JsonElement> PageAsync(HttpClient client)
        => await client.GetFromJsonAsync<JsonElement>(Path);

    /// <summary>The secret names in the caller's "To review" list.</summary>
    public static async Task<string[]> ToReviewAsync(HttpClient client)
        => (await PageAsync(client)).GetProperty("toReview").EnumerateArray()
            .Select(request => request.GetProperty("secretName").GetString()!).ToArray();

    public static async Task<HttpResponseMessage> ApproveAsync(HttpClient reviewer, Guid requestId, int expiresInDays = 0)
        => await DecideAsync(reviewer, await UiApiRequest.AntiforgeryTokenAsync(reviewer), requestId, "approve", JsonContent.Create(new { expiresInDays }));

    public static async Task<HttpResponseMessage> RejectAsync(HttpClient reviewer, Guid requestId)
        => await DecideAsync(reviewer, await UiApiRequest.AntiforgeryTokenAsync(reviewer), requestId, "reject");

    public static async Task<HttpResponseMessage> WithdrawAsync(HttpClient requester, Guid requestId)
        => await DecideAsync(requester, await UiApiRequest.AntiforgeryTokenAsync(requester), requestId, "withdraw");

    /// <summary>
    /// One decision with a token fetched beforehand, for tests that must fetch it while something the fetch
    /// needs still works. <paramref name="decision"/> is the path's last segment.
    /// </summary>
    public static Task<HttpResponseMessage> DecideAsync(HttpClient caller, string token, Guid requestId, string decision, HttpContent? body = null)
        => UiApiRequest.SendWithTokenAsync(caller, token, HttpMethod.Post, $"{Path}/{requestId}/{decision}", body);

    public static async Task<AccessRequestStatus> StatusOfAsync(VaultFixture vault, Guid requestId)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.AsNoTracking()
            .Where(request => request.Id == requestId).Select(request => request.Status).SingleAsync();
    }
}
