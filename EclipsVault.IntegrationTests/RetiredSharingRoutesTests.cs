using System.Net;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Shared with me, the Access requests page and its decisions are the Angular UI's. Their Legacy routes
/// stay unreachable for both people who could have used them: a reviewer and the requester, each with a
/// real session and a valid antiforgery token, aiming at a real pending request. Only
/// <c>POST /AccessRequests/Create</c> remains, for the Denied page's form (<see cref="AccessRequestsApiTests"/>).
/// Each requester signs in from its own address in 198.18.102.0/24, so sign-in budgets never meet.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class RetiredSharingRoutesTests(VaultFixture vault)
{
    // A literal path the Angular UI owns answers 405 to anything but GET and HEAD; a removed action has no
    // route at all, so it answers 404.
    [Theory]
    [InlineData("POST", "/AccessRequests/Approve", HttpStatusCode.NotFound, "198.18.102.1")]
    [InlineData("POST", "/aCcEsSrEqUeStS/aPpRoVe/", HttpStatusCode.NotFound, "198.18.102.2")]
    [InlineData("PUT", "/ACCESSREQUESTS/APPROVE", HttpStatusCode.NotFound, "198.18.102.3")]
    [InlineData("GET", "/accessrequests/approve", HttpStatusCode.NotFound, "198.18.102.4")]
    [InlineData("POST", "/AccessRequests/Reject", HttpStatusCode.NotFound, "198.18.102.5")]
    [InlineData("PATCH", "/aCcEsSrEqUeStS/rEjEcT", HttpStatusCode.NotFound, "198.18.102.6")]
    [InlineData("POST", "/AccessRequests/Cancel", HttpStatusCode.NotFound, "198.18.102.7")]
    [InlineData("DELETE", "/aCcEsSrEqUeStS/cAnCeL", HttpStatusCode.NotFound, "198.18.102.8")]
    [InlineData("POST", "/AccessRequests", HttpStatusCode.MethodNotAllowed, "198.18.102.9")]
    [InlineData("PUT", "/aCcEsSrEqUeStS/iNdEx", HttpStatusCode.MethodNotAllowed, "198.18.102.10")]
    [InlineData("GET", "/AccessRequests/Index/legacy", HttpStatusCode.NotFound, "198.18.102.11")]
    [InlineData("POST", "/Secrets/SharedWithMe", HttpStatusCode.MethodNotAllowed, "198.18.102.12")]
    [InlineData("DELETE", "/sEcReTs/sHaReDwItHmE", HttpStatusCode.MethodNotAllowed, "198.18.102.13")]
    [InlineData("GET", "/sEcReTs/sHaReDwItHmE/legacy", HttpStatusCode.NotFound, "198.18.102.14")]
    public async Task A_retired_sharing_route_reaches_no_handler_for_the_reviewer_or_the_requester(
        string method, string path, HttpStatusCode expected, string source)
    {
        var admin = await vault.AdminClientAsync();
        var username = "it-retired-share-" + Guid.NewGuid().ToString("N");
        var requester = await vault.SignInNewAccountAsync(username, IPAddress.Parse(source), ClearanceLevel.Elevated, "IT-RETIRED-SHARE");
        var details = await LegacySharing.CreateSecretAsync(admin, $"{username}-secret");
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, username);
        var before = await AuditTrail.HeadAsync(vault);

        using var asReviewer = await SendAsync(admin, method, path, pending.Id);
        using var asRequester = await SendAsync(requester.Client, method, path, pending.Id);

        Assert.Equal(expected, asReviewer.StatusCode);
        Assert.Equal(expected, asRequester.StatusCode);
        Assert.Equal(AccessRequestStatus.Pending, await AccessRequestsApi.StatusOfAsync(vault, pending.Id));
        Assert.False(await LegacySharing.HasAnyShareAsync(vault, details));
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.ResourceId == pending.SecretId);
    }

    /// <summary>Sends the fields the Legacy forms posted, naming the request in both the query and the form.</summary>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient caller, string method, string path, Guid requestId)
        => await UiApiRequest.SendWithTokenAsync(caller, await UiApiRequest.AntiforgeryTokenAsync(caller), new HttpMethod(method),
            $"{path}?id={requestId}", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = requestId.ToString(),
                ["ttlDays"] = "7",
                ["note"] = "Decided through a retired route",
            }));
}
