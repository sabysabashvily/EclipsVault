using System.Net;
using System.Text.Json;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Who may see and decide an access request, through the Access requests UI API. Every secret here is
/// Confidential in GLOBAL, so only a trusted address (10.0.0.0/8) with Elevated clearance or above opens
/// it. Requesters belong to another project, so each request is one a share could help.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class AccessRequestReviewerTests(VaultFixture vault)
{
    // An administrator always holds TopSecret clearance, so no administrator is below a secret's
    // sensitivity; an off-network administrator is the administrator who fails the access check.
    [Theory]
    [InlineData("low-clearance-member", ClearanceLevel.Standard, "GLOBAL", "10.99.0.1", "203.0.113.201")]
    [InlineData("other-project-member", ClearanceLevel.Secret, "IT-REVIEW-ELSEWHERE", "10.99.0.2", "203.0.113.202")]
    [InlineData("off-network-member", ClearanceLevel.Elevated, "GLOBAL", "203.0.113.203", "203.0.113.204")]
    [InlineData("off-network-admin", ClearanceLevel.TopSecret, "GLOBAL", "203.0.113.205", "203.0.113.206")]
    public async Task A_reviewer_who_could_not_share_the_secret_neither_sees_nor_decides_its_request(
        string role, ClearanceLevel clearance, string project, string reviewerAddress, string requesterAddress)
    {
        var reviewer = await vault.SignInNewAccountAsync($"it-review-{role}", IPAddress.Parse(reviewerAddress), clearance, project);
        var secretName = $"it-review-secret-{role}";
        var pending = await PendingRequestAsync(secretName, $"it-review-requester-{role}", requesterAddress);
        var before = await AuditTrail.HeadAsync(vault);

        Assert.DoesNotContain(secretName, await AccessRequestsApi.ToReviewAsync(reviewer.Client));
        await AssertRequestNotFoundAsync(await AccessRequestsApi.ApproveAsync(reviewer.Client, pending.Id));
        await AssertRequestNotFoundAsync(await AccessRequestsApi.RejectAsync(reviewer.Client, pending.Id));

        Assert.Equal(AccessRequestStatus.Pending, await AccessRequestsApi.StatusOfAsync(vault, pending.Id));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task The_requester_neither_sees_nor_decides_their_own_request_even_as_a_qualified_administrator()
    {
        var requester = await vault.SignInNewAccountAsync("it-review-own", IPAddress.Parse("10.99.0.3"), ClearanceLevel.TopSecret);
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-review-secret-own", sensitivity: "Confidential");
        // Inserted directly: filing refuses an account that can already open the secret, but such a request can exist.
        var pending = await LegacyAccessRequests.PendingAsync(vault, details, "it-review-own");
        var before = await AuditTrail.HeadAsync(vault);

        Assert.DoesNotContain("it-review-secret-own", await AccessRequestsApi.ToReviewAsync(requester.Client));
        await AssertRequestNotFoundAsync(await AccessRequestsApi.ApproveAsync(requester.Client, pending.Id));
        await AssertRequestNotFoundAsync(await AccessRequestsApi.RejectAsync(requester.Client, pending.Id));

        Assert.Equal(AccessRequestStatus.Pending, await AccessRequestsApi.StatusOfAsync(vault, pending.Id));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Theory]
    [InlineData("member", ClearanceLevel.Elevated, "GLOBAL", "10.99.0.4", "203.0.113.207")]
    [InlineData("admin", ClearanceLevel.TopSecret, "IT-REVIEW-ADMINS", "10.99.0.5", "203.0.113.208")]
    public async Task A_reviewer_who_could_share_the_secret_sees_and_approves_its_request(
        string role, ClearanceLevel clearance, string project, string reviewerAddress, string requesterAddress)
    {
        var reviewer = await vault.SignInNewAccountAsync($"it-review-qualified-{role}", IPAddress.Parse(reviewerAddress), clearance, project);
        var secretName = $"it-review-secret-qualified-{role}";
        var pending = await PendingRequestAsync(secretName, $"it-review-requester-qualified-{role}", requesterAddress);

        Assert.Contains(secretName, await AccessRequestsApi.ToReviewAsync(reviewer.Client));
        var response = await AccessRequestsApi.ApproveAsync(reviewer.Client, pending.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AccessRequestStatus.Approved, await AccessRequestsApi.StatusOfAsync(vault, pending.Id));
        using var scope = vault.Services.CreateScope();
        var share = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SecretGrants.AsNoTracking()
            .SingleAsync(share => share.SecretId == pending.SecretId);
        Assert.Equal(pending.RequesterUserId, share.GranteeUserId);
        Assert.Equal($"it-review-qualified-{role}", share.GrantedBy);
    }

    [Theory]
    [InlineData("approve", "10.99.1.1", "203.0.113.221")]
    [InlineData("reject", "10.99.1.2", "203.0.113.222")]
    [InlineData("withdraw", "10.99.1.3", "203.0.113.223")]
    public async Task A_request_the_caller_cannot_decide_or_withdraw_is_refused_exactly_like_an_unknown_one(
        string decision, string callerAddress, string requesterAddress)
    {
        var caller = await vault.SignInNewAccountAsync(
            $"it-review-identical-{decision}", IPAddress.Parse(callerAddress), ClearanceLevel.Secret, "IT-REVIEW-ELSEWHERE");
        var pending = await PendingRequestAsync(
            $"it-review-secret-identical-{decision}", $"it-review-requester-identical-{decision}", requesterAddress);
        var before = await AuditTrail.HeadAsync(vault);

        var undecidable = await DecideAsync(caller, decision, pending.Id);
        var unknown = await DecideAsync(caller, decision, Guid.NewGuid());

        await AssertRequestNotFoundAsync(undecidable);
        await AssertRequestNotFoundAsync(unknown);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await undecidable.Content.ReadAsStringAsync());
        Assert.Equal(AccessRequestStatus.Pending, await AccessRequestsApi.StatusOfAsync(vault, pending.Id));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Even_a_qualified_reviewer_cannot_withdraw_someone_elses_request()
    {
        var reviewer = await vault.SignInNewAccountAsync("it-review-withdraw-other", IPAddress.Parse("10.99.0.8"), ClearanceLevel.TopSecret);
        var pending = await PendingRequestAsync("it-review-secret-withdraw-other", "it-review-requester-withdraw-other", "203.0.113.230");
        Assert.Contains("it-review-secret-withdraw-other", await AccessRequestsApi.ToReviewAsync(reviewer.Client));
        var before = await AuditTrail.HeadAsync(vault);

        await AssertRequestNotFoundAsync(await AccessRequestsApi.WithdrawAsync(reviewer.Client, pending.Id));

        Assert.Equal(AccessRequestStatus.Pending, await AccessRequestsApi.StatusOfAsync(vault, pending.Id));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    private async Task<AccessRequest> PendingRequestAsync(string secretName, string requester, string requesterAddress)
    {
        await vault.SignInNewAccountAsync(requester, IPAddress.Parse(requesterAddress), ClearanceLevel.Elevated, "IT-REVIEW-REQUESTERS");
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), secretName, sensitivity: "Confidential");
        return await LegacyAccessRequests.PendingAsync(vault, details, requester);
    }

    private static Task<HttpResponseMessage> DecideAsync(VaultSession caller, string decision, Guid requestId) => decision switch
    {
        "approve" => AccessRequestsApi.ApproveAsync(caller.Client, requestId),
        "reject" => AccessRequestsApi.RejectAsync(caller.Client, requestId),
        _ => AccessRequestsApi.WithdrawAsync(caller.Client, requestId),
    };

    private static async Task AssertRequestNotFoundAsync(HttpResponseMessage response)
    {
        await UiApiFailureContractTests.AssertProblemAsync(response, 404, "request_not_found");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("That request was not found.", problem.RootElement.GetProperty("title").GetString());
    }
}
