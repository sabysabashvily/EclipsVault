using System.Security.Claims;
using EclipsVault.Core.Application.Abac;
using EclipsVault.Core.Application.AccessRequests;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EclipsVault.Tests.AccessRequests;

/// <summary>
/// The reviewer rule for a request on a secret in project PAYMENTS. Whether the caller passes the secret's
/// access check (clearance, network, time) is the handler's decision, given here as a column.
/// </summary>
public sealed class AccessRequestReviewersTests
{
    private const string Requester = "0d0d0d0d-0000-0000-0000-000000000001";
    private const string SomeoneElse = "0d0d0d0d-0000-0000-0000-000000000002";

    private static readonly AccessRequestForReview Candidate = new(
        new AccessRequestDto(Guid.NewGuid(), Guid.NewGuid(), "payments-api-key", "PAYMENTS", Guid.Parse(Requester), "requester",
            "Need this for deployment", null, AccessRequestStatus.Pending, DateTimeOffset.UnixEpoch, null, null, null),
        new SecretSummaryDto(Guid.NewGuid(), "payments-api-key", "PAYMENTS", SecretEnvironment.Production,
            SensitivityLevel.Confidential, DateTimeOffset.UnixEpoch, null));

    [Theory]
    // caller,     clearance,                 project,    passes the access check, may decide
    [InlineData(SomeoneElse, ClearanceLevel.TopSecret, "TEAM-B", true, true)]
    [InlineData(SomeoneElse, ClearanceLevel.Elevated, "PAYMENTS", true, true)]
    [InlineData(SomeoneElse, ClearanceLevel.Elevated, "payments", true, true)]
    [InlineData(SomeoneElse, ClearanceLevel.TopSecret, "TEAM-B", false, false)]
    [InlineData(SomeoneElse, ClearanceLevel.Standard, "PAYMENTS", false, false)]
    [InlineData(SomeoneElse, ClearanceLevel.Secret, "TEAM-B", true, false)]
    [InlineData(Requester, ClearanceLevel.TopSecret, "PAYMENTS", true, false)]
    public async Task A_reviewer_is_someone_else_who_could_share_the_secret_from_its_page(
        string caller, ClearanceLevel clearance, string project, bool passesAccessCheck, bool mayDecide)
    {
        var reviewers = new AccessRequestReviewers(new PendingRequests(Candidate), new AccessCheck(Candidate.Secret, passesAccessCheck));

        Assert.Equal(mayDecide, await reviewers.MayDecideAsync(Caller(caller, clearance, project), Candidate.Request.Id, default));
    }

    [Fact]
    public async Task The_queue_asks_the_access_check_as_a_listing_so_a_hidden_request_is_no_denied_read()
    {
        var access = new AccessCheck(Candidate.Secret, passes: false);
        var reviewers = new AccessRequestReviewers(new PendingRequests(Candidate), access);

        await reviewers.QueueAsync(Caller(SomeoneElse, ClearanceLevel.TopSecret, "TEAM-B"), default);

        Assert.Equal([AccessKind.Enumerate], access.Asked);
    }

    [Fact]
    public async Task Deciding_one_request_asks_the_access_check_as_a_read()
    {
        var access = new AccessCheck(Candidate.Secret, passes: false);
        var reviewers = new AccessRequestReviewers(new PendingRequests(Candidate), access);

        await reviewers.MayDecideAsync(Caller(SomeoneElse, ClearanceLevel.TopSecret, "TEAM-B"), Candidate.Request.Id, default);

        Assert.Equal([AccessKind.Read], access.Asked);
    }

    private static ClaimsPrincipal Caller(string id, ClearanceLevel clearance, string project)
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, id),
            new Claim(VaultClaimTypes.Clearance, ((int)clearance).ToString()),
            new Claim(VaultClaimTypes.Project, project),
        ], "test"));

    /// <summary>
    /// Answers the secret's access check for one secret, and records how it was asked: as a listing
    /// (Enumerate) or as a read. The <see cref="VaultPolicies.SecretAccess"/> policy is the Read requirement.
    /// </summary>
    private sealed class AccessCheck(SecretSummaryDto secret, bool passes) : IAuthorizationService
    {
        public List<AccessKind> Asked { get; } = [];

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            Asked.Add(AccessKind.Read);
            return Answer(policyName == VaultPolicies.SecretAccess && Equals(resource, secret));
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            var requirement = Assert.IsType<SecretAccessRequirement>(Assert.Single(requirements));
            Asked.Add(requirement.Kind);
            return Answer(Equals(resource, secret));
        }

        private Task<AuthorizationResult> Answer(bool askedAboutTheSecret)
            => Task.FromResult(passes && askedAboutTheSecret ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    /// <summary>One pending request, the only thing the reviewer rule reads from Core.</summary>
    private sealed class PendingRequests(AccessRequestForReview pending) : IAccessRequestService
    {
        public Task<IReadOnlyList<AccessRequestForReview>> ListPendingForReviewAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AccessRequestForReview>>([pending]);

        public Task<AccessRequestForReview?> FindForReviewAsync(Guid id, CancellationToken ct)
            => Task.FromResult(id == pending.Request.Id ? pending : null);

        public Task<AccessRequestRefusal?> FilingRefusalAsync(Guid secretId, Guid requesterUserId, CancellationToken ct) => throw new NotSupportedException();

        public Task<SecretSummaryDto> SecretToRequestAsync(Guid secretId, CancellationToken ct) => throw new NotSupportedException();

        public Task<AccessRequestCreateResult> CreateAsync(Guid secretId, Guid requesterUserId, string requesterUsername, string reason,
            IReadOnlyList<string> denialReasons, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<AccessRequestDto>> ListMineAsync(Guid requesterUserId, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> ApproveAsync(Guid id, int expiresInDays, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> RejectAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> CancelAsync(Guid id, Guid requesterUserId, CancellationToken ct) => throw new NotSupportedException();
    }
}
