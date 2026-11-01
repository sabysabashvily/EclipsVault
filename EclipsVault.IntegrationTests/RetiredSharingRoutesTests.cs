using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Legacy Access requests and Shared with me handlers are gone: a real session with a valid
/// antiforgery token, in any letter case and with any method, reaches no handler and changes no
/// request, share or audit row. Each requester signs in from its own address in 198.18.102.0/24,
/// so sign-in budgets never meet.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class RetiredSharingRoutesTests(VaultFixture vault)
{
    [Theory]
    [InlineData("/AccessRequests/Approve", HttpStatusCode.NotFound, "198.18.102.1")]
    [InlineData("/aCcEsSrEqUeStS/aPpRoVe/", HttpStatusCode.NotFound, "198.18.102.2")]
    [InlineData("/AccessRequests/Approve/{0}", HttpStatusCode.NotFound, "198.18.102.3")]
    [InlineData("/AccessRequests/Reject", HttpStatusCode.NotFound, "198.18.102.4")]
    [InlineData("/ACCESSREQUESTS/REJECT/{0}", HttpStatusCode.NotFound, "198.18.102.5")]
    [InlineData("/AccessRequests", HttpStatusCode.MethodNotAllowed, "198.18.102.6")]
    [InlineData("/accessrequests/index", HttpStatusCode.MethodNotAllowed, "198.18.102.7")]
    [InlineData("/AccessRequests/Index/{0}", HttpStatusCode.NotFound, "198.18.102.8")]
    [InlineData("/sEcReTs/SharedWithMe", HttpStatusCode.MethodNotAllowed, "198.18.102.9")]
    public async Task A_retired_reviewer_write_decides_nothing_with_a_real_session_and_valid_antiforgery(
        string path, HttpStatusCode expected, string requesterAddress)
    {
        var admin = await vault.AdminClientAsync();
        var pending = await PendingRequestAsync(admin, $"it-retired-review-{requesterAddress[^1..]}", requesterAddress);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await PostLegacyDecisionAsync(admin, string.Format(path, pending.RequestId), pending.RequestId);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(AccessRequestStatus.Pending, await AccessRequestsApi.StatusOfAsync(vault, pending.RequestId));
        Assert.Equal(0, await SharesForAsync(pending.RequesterId));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Theory]
    [InlineData("/AccessRequests/Cancel", HttpStatusCode.NotFound, "198.18.102.20")]
    [InlineData("/aCcEsSrEqUeStS/cAnCeL/{0}/", HttpStatusCode.NotFound, "198.18.102.21")]
    public async Task A_retired_withdrawal_withdraws_nothing_with_the_requesters_session_and_valid_antiforgery(
        string path, HttpStatusCode expected, string requesterAddress)
    {
        var admin = await vault.AdminClientAsync();
        var pending = await PendingRequestAsync(admin, $"it-retired-cancel-{requesterAddress[^2..]}", requesterAddress);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await PostLegacyDecisionAsync(pending.Requester, string.Format(path, pending.RequestId), pending.RequestId);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(AccessRequestStatus.Pending, await AccessRequestsApi.StatusOfAsync(vault, pending.RequestId));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Theory]
    [InlineData("GET", "/AccessRequests/Approve/{0}", HttpStatusCode.NotFound, "198.18.102.40")]
    [InlineData("PUT", "/aCcEsSrEqUeStS/aPpRoVe/{0}", HttpStatusCode.NotFound, "198.18.102.41")]
    [InlineData("PATCH", "/AccessRequests/Reject/{0}", HttpStatusCode.NotFound, "198.18.102.42")]
    [InlineData("DELETE", "/ACCESSREQUESTS/CANCEL/{0}", HttpStatusCode.NotFound, "198.18.102.43")]
    [InlineData("GET", "/AccessRequests/Create", HttpStatusCode.MethodNotAllowed, "198.18.102.44")]
    [InlineData("PUT", "/accessrequests/create", HttpStatusCode.MethodNotAllowed, "198.18.102.45")]
    [InlineData("DELETE", "/Secrets/SharedWithMe", HttpStatusCode.MethodNotAllowed, "198.18.102.46")]
    public async Task No_other_method_or_case_reaches_a_retired_sharing_handler(
        string method, string path, HttpStatusCode expected, string requesterAddress)
    {
        var admin = await vault.AdminClientAsync();
        var pending = await PendingRequestAsync(admin, $"it-retired-verb-{requesterAddress[^2..]}", requesterAddress);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SendLegacyFormAsync(admin, new HttpMethod(method), string.Format(path, pending.RequestId), pending.RequestId);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(AccessRequestStatus.Pending, await AccessRequestsApi.StatusOfAsync(vault, pending.RequestId));
        Assert.Equal(0, await SharesForAsync(pending.RequesterId));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    private sealed record PendingRequest(Guid RequestId, Guid RequesterId, HttpClient Requester);

    private async Task<PendingRequest> PendingRequestAsync(HttpClient admin, string username, string requesterAddress)
    {
        var requester = await vault.SignInNewAccountAsync(username, IPAddress.Parse(requesterAddress), ClearanceLevel.Elevated, "IT-RETIRED");
        var request = await LegacyAccessRequests.PendingAsync(vault, await LegacySharing.CreateSecretAsync(admin, username), username);
        return new PendingRequest(request.Id, request.RequesterUserId, requester.Client);
    }

    private static Task<HttpResponseMessage> PostLegacyDecisionAsync(HttpClient caller, string path, Guid requestId)
        => SendLegacyFormAsync(caller, HttpMethod.Post, path, requestId);

    // The fields the retired Legacy forms posted, with the token both as a form field and as the header,
    // so a refusal can only mean that no handler answers, never that antiforgery failed.
    private static async Task<HttpResponseMessage> SendLegacyFormAsync(HttpClient caller, HttpMethod method, string path, Guid requestId)
    {
        var token = await UiApiRequest.AntiforgeryTokenAsync(caller);
        return await UiApiRequest.SendWithTokenAsync(caller, token, method, path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = requestId.ToString(),
            ["ttlDays"] = "30",
            ["note"] = "Retired decision",
        }));
    }

    private async Task<int> SharesForAsync(Guid recipientId)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SecretGrants.AsNoTracking()
            .CountAsync(share => share.GranteeUserId == recipientId);
    }
}
