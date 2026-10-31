using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.AccessRequests;

/// <summary>An access request as shown in the requester's list and the reviewer's queue.</summary>
public sealed record AccessRequestDto(
    Guid Id,
    Guid SecretId,
    string SecretName,
    string ProjectKey,
    Guid RequesterUserId,
    string RequesterUsername,
    string Reason,
    string? DeniedReasons,
    AccessRequestStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    string? DecidedBy,
    string? DecisionNote)
{
    public static AccessRequestDto From(AccessRequest r) => new(
        r.Id, r.SecretId, r.SecretName, r.ProjectKey, r.RequesterUserId, r.RequesterUsername, r.Reason, r.DeniedReasons,
        r.Status, r.CreatedAtUtc, r.DecidedAtUtc, r.DecidedBy, r.DecisionNote);
}

/// <summary>A request with its secret as it is now, whose attributes decide who may review the request.</summary>
public sealed record AccessRequestForReview(AccessRequestDto Request, SecretSummaryDto Secret);

/// <summary>Outcome of filing a request: whether it was created, and if not, why.</summary>
public sealed record AccessRequestCreateResult(bool Created, string? Error)
{
    public static readonly AccessRequestCreateResult Ok = new(true, null);

    public static AccessRequestCreateResult Failed(string error) => new(false, error);
}

/// <summary>
/// Self-service access requests (ADR-0027). An account denied a secret only by its project files a request;
/// a reviewer approves it, which shares the secret, or rejects it. Who may review is the host's to decide,
/// because it runs the secret's access check: Core only lists the candidates.
/// </summary>
public interface IAccessRequestService
{
    /// <summary>
    /// Why this account may not request the secret, or null when it may. Writes no audit row of its own; an
    /// unknown, shredded or expired secret is a <see cref="SecretNotFoundException"/>, and a honey token trips.
    /// </summary>
    Task<AccessRequestRefusal?> FilingRefusalAsync(Guid secretId, Guid requesterUserId, CancellationToken ct);

    /// <summary>
    /// The secret's attributes, for the host's access check that supplies the denial reasons. Writes no
    /// audit row of its own; a honey token trips.
    /// </summary>
    Task<SecretSummaryDto> SecretToRequestAsync(Guid secretId, CancellationToken ct);

    /// <summary>
    /// Files a request. <paramref name="denialReasons"/> are the access check's own reasons, never text the
    /// requester sent. A honey token trips.
    /// </summary>
    Task<AccessRequestCreateResult> CreateAsync(
        Guid secretId, Guid requesterUserId, string requesterUsername, string reason,
        IReadOnlyList<string> denialReasons, CancellationToken ct);

    /// <summary>Requests filed by this user, newest first.</summary>
    Task<IReadOnlyList<AccessRequestDto>> ListMineAsync(Guid requesterUserId, CancellationToken ct);

    /// <summary>Every pending request, newest first, for the host to narrow to those its caller may review.</summary>
    Task<IReadOnlyList<AccessRequestForReview>> ListPendingForReviewAsync(CancellationToken ct);

    /// <summary>One request in any status, for the host to check its caller may review it.</summary>
    Task<AccessRequestForReview?> FindForReviewAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Approves a pending request and shares the secret with the requester. <paramref name="expiresInDays"/>
    /// 0 means the share never expires. Returns false when the request is no longer pending, including when
    /// another decision won. A request a share could no longer help is an <see cref="AccessRequestIneligibleException"/>,
    /// and one whose secret was shredded or has expired a <see cref="SecretNotFoundException"/>.
    /// </summary>
    Task<bool> ApproveAsync(Guid id, int expiresInDays, CancellationToken ct);

    Task<bool> RejectAsync(Guid id, CancellationToken ct);

    /// <summary>Lets the original requester withdraw their own pending request.</summary>
    Task<bool> CancelAsync(Guid id, Guid requesterUserId, CancellationToken ct);
}
