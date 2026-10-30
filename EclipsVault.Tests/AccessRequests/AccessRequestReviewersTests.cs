using System.Security.Claims;
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
    private static readonly Guid Requester = Guid.Parse("0d0d0d0d-0000-0000-0000-000000000001");
    private static readonly Guid SomeoneElse = Guid.Parse("0d0d0d0d-0000-0000-0000-000000000002");

    private static readonly AccessRequestForReview Candidate = new(
        new AccessRequestDto(Guid.NewGuid(), Guid.NewGuid(), "payments-api-key", "PAYMENTS", Requester, "requester",
            "Need this for deployment", null, AccessRequestStatus.Pending, DateTimeOffset.UnixEpoch, null, null, null),
        new SecretSummaryDto(Guid.NewGuid(), "payments-api-key", "PAYMENTS", SecretEnvironment.Production,
            SensitivityLevel.Confidential, DateTimeOffset.UnixEpoch, null));

    [Theory]
    [InlineData(false, ClearanceLevel.TopSecret, "TEAM-B", true, true)]
    [InlineData(false, ClearanceLevel.Elevated, "PAYMENTS", true, true)]
    [InlineData(false, ClearanceLevel.Elevated, "payments", true, true)]
    [InlineData(false, ClearanceLevel.TopSecret, "TEAM-B", false, false)]
    [InlineData(false, ClearanceLevel.Standard, "PAYMENTS", false, false)]
    [InlineData(false, ClearanceLevel.Secret, "TEAM-B", true, false)]
    [InlineData(true, ClearanceLevel.TopSecret, "PAYMENTS", true, false)]
    public async Task A_reviewer_is_someone_else_who_could_share_the_secret_from_its_page(
        bool callerIsRequester, ClearanceLevel clearance, string project, bool passesAccessCheck, bool mayDecide)
    {
        var caller = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, (callerIsRequester ? Requester : SomeoneElse).ToString()),
            new Claim(VaultClaimTypes.Clearance, ((int)clearance).ToString()),
            new Claim(VaultClaimTypes.Project, project),
        ], "test"));
        var reviewers = new AccessRequestReviewers(requests: null!, new AccessCheck(Candidate.Secret, passesAccessCheck));

        Assert.Equal(mayDecide, await reviewers.MayDecideAsync(caller, Candidate));
    }

    /// <summary>Answers the secret-access policy for one secret; refuses every other question.</summary>
    private sealed class AccessCheck(SecretSummaryDto secret, bool passes) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(passes && policyName == VaultPolicies.SecretAccess && Equals(resource, secret)
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());
    }
}
