using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.AccessRequests;

/// <summary>An access request as shown in the requester's list and the reviewer's queue.</summary>
public sealed record AccessRequestDto(
    Guid Id,
    Guid SecretId,
    string SecretName,
    string ProjectKey,
    string RequesterUsername,
    string Reason,
    string? DeniedReasons,
    AccessRequestStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? DecidedBy,
    string? DecisionNote);

/// <summary>Outcome of filing a request: whether it was created, and if not, why.</summary>
public sealed record AccessRequestCreateResult(bool Created, string? Error)
{
    public static readonly AccessRequestCreateResult Ok = new(true, null);

    public static AccessRequestCreateResult Failed(string error) => new(false, error);
}

/// <summary>
/// Self-service access requests. A denied user files a request; a reviewer (an admin, or a
/// member of the secret's project) approves it — which creates an ordinary grant — or rejects
/// it. Every transition is audited.
/// </summary>
public interface IAccessRequestService
{
    Task<AccessRequestCreateResult> CreateAsync(
        Guid secretId, string secretName, string projectKey,
        Guid requesterUserId, string requesterUsername, string reason, string? deniedReasons, CancellationToken ct);

    /// <summary>Requests filed by this user, newest first.</summary>
    Task<IReadOnlyList<AccessRequestDto>> ListMineAsync(Guid requesterUserId, CancellationToken ct);

    /// <summary>Pending requests this reviewer may act on: all of them for an admin, otherwise their project's.</summary>
    Task<IReadOnlyList<AccessRequestDto>> ListToReviewAsync(bool isAdmin, string reviewerProject, CancellationToken ct);

    Task<AccessRequestDto?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Approves a pending request, creating a grant for the requester. Returns false if it was not pending.</summary>
    Task<bool> ApproveAsync(Guid id, int expiresInDays, CancellationToken ct);

    Task<bool> RejectAsync(Guid id, CancellationToken ct);

    /// <summary>Lets the original requester withdraw their own pending request.</summary>
    Task<bool> CancelAsync(Guid id, Guid requesterUserId, CancellationToken ct);
}
