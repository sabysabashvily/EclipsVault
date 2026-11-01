using System.Net;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// My activity's Sharing entries for the access-request changes the audit interceptor records: each
/// account sees only what it did itself, in the activity's own words. Requesters sign in from
/// 198.18.104.0/24 and reviewers from the trusted 10.99.104.0/24, so sign-in budgets never meet.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SharingActivityTests(VaultFixture vault)
{
    [Fact]
    public async Task Approving_shows_with_its_share_only_in_the_reviewers_activity()
    {
        var requester = await vault.SignInNewAccountAsync("it-activity-approve-requester", IPAddress.Parse("198.18.104.1"), ClearanceLevel.Elevated, "IT-ACTIVITY");
        var reviewer = await vault.SignInNewAccountAsync("it-activity-approve-reviewer", IPAddress.Parse("10.99.104.1"), ClearanceLevel.TopSecret);
        var requestId = await FileAsync(requester.Client, "it-activity-approve-secret");

        Assert.Equal(HttpStatusCode.NoContent, (await AccessRequestsApi.ApproveAsync(reviewer.Client, requestId)).StatusCode);

        Assert.Equal(["Requested access to a secret"], await SharingTitlesAsync(requester.Client));
        // One transaction writes both rows at the same instant; the share's row takes the later sequence.
        Assert.Equal(["Shared a secret", "Approved an access request"], await SharingTitlesAsync(reviewer.Client));
    }

    [Fact]
    public async Task Rejecting_shows_only_in_the_reviewers_activity()
    {
        var requester = await vault.SignInNewAccountAsync("it-activity-reject-requester", IPAddress.Parse("198.18.104.2"), ClearanceLevel.Elevated, "IT-ACTIVITY");
        var reviewer = await vault.SignInNewAccountAsync("it-activity-reject-reviewer", IPAddress.Parse("10.99.104.2"), ClearanceLevel.TopSecret);
        var requestId = await FileAsync(requester.Client, "it-activity-reject-secret");

        Assert.Equal(HttpStatusCode.NoContent, (await AccessRequestsApi.RejectAsync(reviewer.Client, requestId)).StatusCode);

        Assert.Equal(["Requested access to a secret"], await SharingTitlesAsync(requester.Client));
        Assert.Equal(["Rejected an access request"], await SharingTitlesAsync(reviewer.Client));
    }

    [Fact]
    public async Task Withdrawing_shows_in_the_requesters_activity_newest_first()
    {
        var requester = await vault.SignInNewAccountAsync("it-activity-withdraw-requester", IPAddress.Parse("198.18.104.3"), ClearanceLevel.Elevated, "IT-ACTIVITY");
        var requestId = await FileAsync(requester.Client, "it-activity-withdraw-secret");

        Assert.Equal(HttpStatusCode.NoContent, (await AccessRequestsApi.WithdrawAsync(requester.Client, requestId)).StatusCode);

        Assert.Equal(["Cancelled an access request", "Requested access to a secret"], await SharingTitlesAsync(requester.Client));
    }

    /// <summary>Files a request from the Denied page's form for a new secret outside the requester's project.</summary>
    private async Task<Guid> FileAsync(HttpClient requester, string secretName)
    {
        var secretId = LegacySharing.SecretIdOf(await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), secretName));
        var filed = await LegacyAccessRequests.FileAsync(requester, await LegacyAccessRequests.TokenAsync(requester), secretId);
        Assert.Equal("/AccessRequests?filed=1", filed.Headers.Location?.OriginalString);

        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.AsNoTracking()
            .Where(request => request.SecretId == secretId).Select(request => request.Id).SingleAsync();
    }

    /// <summary>The titles of the caller's Sharing entries on My activity's first page, newest first.</summary>
    private static async Task<string[]> SharingTitlesAsync(HttpClient client)
    {
        using var activity = JsonDocument.Parse(await client.GetStringAsync("/api/ui/activity"));
        return activity.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("category").GetString() == "Sharing")
            .Select(item => item.GetProperty("title").GetString()!)
            .ToArray();
    }
}
