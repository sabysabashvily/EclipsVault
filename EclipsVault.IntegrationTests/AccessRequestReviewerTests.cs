using System.Net;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Who may see and decide an access request, through Legacy's Access requests page. Every secret here is
/// Confidential in GLOBAL, so only a trusted address (10.0.0.0/8) with Elevated clearance or above opens
/// it. Requesters belong to another project, so each request is one a share could help.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class AccessRequestReviewerTests(VaultFixture vault)
{
    private const string NotFound = "That request was not found.";

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

        Assert.DoesNotContain(secretName, await LegacyAccessRequests.ToReviewPanelAsync(reviewer.Client));
        await AssertRefusedAsNotFoundAsync(reviewer.Client, "Approve", pending.Id);
        await AssertRefusedAsNotFoundAsync(reviewer.Client, "Reject", pending.Id);

        Assert.Equal(AccessRequestStatus.Pending, await StatusOfAsync(pending.Id));
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

        Assert.DoesNotContain("it-review-secret-own", await LegacyAccessRequests.ToReviewPanelAsync(requester.Client));
        await AssertRefusedAsNotFoundAsync(requester.Client, "Approve", pending.Id);
        await AssertRefusedAsNotFoundAsync(requester.Client, "Reject", pending.Id);

        Assert.Equal(AccessRequestStatus.Pending, await StatusOfAsync(pending.Id));
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

        Assert.Contains(secretName, await LegacyAccessRequests.ToReviewPanelAsync(reviewer.Client));
        var response = await LegacyAccessRequests.PostAsync(reviewer.Client, "Approve", await LegacyAccessRequests.TokenAsync(reviewer.Client), pending.Id);

        Assert.Equal("/AccessRequests", response.Headers.Location?.OriginalString);
        Assert.Equal(AccessRequestStatus.Approved, await StatusOfAsync(pending.Id));
        using var scope = vault.Services.CreateScope();
        var share = await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SecretGrants.AsNoTracking()
            .SingleAsync(share => share.SecretId == pending.SecretId);
        Assert.Equal(pending.RequesterUserId, share.GranteeUserId);
        Assert.Equal($"it-review-qualified-{role}", share.GrantedBy);
    }

    [Fact]
    public async Task A_request_the_reviewer_cannot_decide_is_refused_exactly_like_an_unknown_one()
    {
        var reviewer = await vault.SignInNewAccountAsync(
            "it-review-identical", IPAddress.Parse("10.99.0.6"), ClearanceLevel.Secret, "IT-REVIEW-ELSEWHERE");
        var pending = await PendingRequestAsync("it-review-secret-identical", "it-review-requester-identical", "203.0.113.209");
        var token = await LegacyAccessRequests.TokenAsync(reviewer.Client);
        var before = await AuditTrail.HeadAsync(vault);

        var undecidable = await LegacyAccessRequests.PostAsync(reviewer.Client, "Approve", token, pending.Id);
        var undecidablePage = await reviewer.Client.GetStringAsync("/AccessRequests");
        var unknown = await LegacyAccessRequests.PostAsync(reviewer.Client, "Approve", token, Guid.NewGuid());
        var unknownPage = await reviewer.Client.GetStringAsync("/AccessRequests");

        Assert.Equal(unknown.StatusCode, undecidable.StatusCode);
        Assert.Equal(unknown.Headers.Location?.OriginalString, undecidable.Headers.Location?.OriginalString);
        Assert.Contains(NotFound, undecidablePage);
        Assert.Contains(NotFound, unknownPage);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    private async Task<AccessRequest> PendingRequestAsync(string secretName, string requester, string requesterAddress)
    {
        await vault.SignInNewAccountAsync(requester, IPAddress.Parse(requesterAddress), ClearanceLevel.Elevated, "IT-REVIEW-REQUESTERS");
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), secretName, sensitivity: "Confidential");
        return await LegacyAccessRequests.PendingAsync(vault, details, requester);
    }

    private async Task AssertRefusedAsNotFoundAsync(HttpClient reviewer, string action, Guid requestId)
    {
        var response = await LegacyAccessRequests.PostAsync(reviewer, action, await LegacyAccessRequests.TokenAsync(reviewer), requestId);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/AccessRequests", response.Headers.Location?.OriginalString);
        Assert.Contains(NotFound, await reviewer.GetStringAsync("/AccessRequests"));
    }

    private async Task<AccessRequestStatus> StatusOfAsync(Guid requestId)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AccessRequests.AsNoTracking()
            .Where(request => request.Id == requestId).Select(request => request.Status).SingleAsync();
    }
}
