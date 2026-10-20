using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.AccessRequests;

/// <summary>
/// Drives the access-request workflow. Approval delegates to <see cref="ISecretGrantService"/>,
/// so an approved request becomes an ordinary grant governed by the same ABAC engine — the
/// project boundary is crossed, but clearance, network, and time rules still apply.
/// </summary>
public sealed class AccessRequestService : IAccessRequestService
{
    private const int MinReasonLength = 3;
    private const int MaxReasonLength = 500;

    private readonly IAccessRequestRepository _requests;
    private readonly ISecretGrantService _grants;
    private readonly INotificationService _notifications;
    private readonly IAuditSink _audit;
    private readonly TimeProvider _clock;

    public AccessRequestService(
        IAccessRequestRepository requests,
        ISecretGrantService grants,
        INotificationService notifications,
        IAuditSink audit,
        TimeProvider clock)
    {
        _requests = requests;
        _grants = grants;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
    }

    public async Task<AccessRequestCreateResult> CreateAsync(
        Guid secretId, string secretName, string projectKey,
        Guid requesterUserId, string requesterUsername, string reason, string? deniedReasons, CancellationToken ct)
    {
        reason = reason.Trim();
        if (reason.Length < MinReasonLength)
        {
            return AccessRequestCreateResult.Failed("Add a short reason so a reviewer can decide.");
        }

        if (reason.Length > MaxReasonLength)
        {
            reason = reason[..MaxReasonLength];
        }

        if (await _requests.HasPendingAsync(secretId, requesterUserId, ct))
        {
            return AccessRequestCreateResult.Failed("You already have a pending request for this secret.");
        }

        var request = new AccessRequest
        {
            Id = Guid.NewGuid(),
            SecretId = secretId,
            SecretName = secretName,
            ProjectKey = projectKey,
            RequesterUserId = requesterUserId,
            RequesterUsername = requesterUsername,
            Reason = reason,
            DeniedReasons = string.IsNullOrWhiteSpace(deniedReasons) ? null : deniedReasons,
            Status = AccessRequestStatus.Pending,
            CreatedAtUtc = _clock.GetUtcNow()
        };

        await _requests.AddAsync(request, ct);
        await AuditAsync(AuditAction.AccessRequested, request, $"Access to '{secretName}' requested", ct);
        return AccessRequestCreateResult.Ok;
    }

    public async Task<IReadOnlyList<AccessRequestDto>> ListMineAsync(Guid requesterUserId, CancellationToken ct)
        => (await _requests.ListByRequesterAsync(requesterUserId, ct)).Select(Map).ToList();

    public async Task<IReadOnlyList<AccessRequestDto>> ListToReviewAsync(bool isAdmin, string reviewerProject, CancellationToken ct)
    {
        var pending = isAdmin
            ? await _requests.ListPendingAsync(ct)
            : await _requests.ListPendingForProjectAsync(reviewerProject, ct);
        return pending.Select(Map).ToList();
    }

    public async Task<AccessRequestDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var request = await _requests.FindAsync(id, ct);
        return request is null ? null : Map(request);
    }

    public async Task<bool> ApproveAsync(Guid id, string reviewerUsername, int? ttlDays, string? note, CancellationToken ct)
    {
        var request = await _requests.FindAsync(id, ct);
        if (request is null || request.Status != AccessRequestStatus.Pending)
        {
            return false;
        }

        // Skip re-granting if a live grant already covers them; otherwise create it. A failure
        // here (SharingException) propagates so the reviewer sees it and the request stays pending.
        if (!await _grants.HasActiveGrantAsync(request.RequesterUserId, request.SecretId, ct))
        {
            await _grants.GrantAsync(request.SecretId, request.RequesterUsername, ttlDays, ct);
        }

        Decide(request, AccessRequestStatus.Approved, reviewerUsername, note);
        await _requests.UpdateAsync(request, ct);
        await AuditAsync(AuditAction.AccessRequestApproved, request,
            $"Access to '{request.SecretName}' approved for {request.RequesterUsername}", ct);
        await _notifications.NotifyAccessRequestDecidedAsync(
            request.RequesterUserId, request.SecretName, approved: true, reviewerUsername, note, ct);
        return true;
    }

    public async Task<bool> RejectAsync(Guid id, string reviewerUsername, string? note, CancellationToken ct)
    {
        var request = await _requests.FindAsync(id, ct);
        if (request is null || request.Status != AccessRequestStatus.Pending)
        {
            return false;
        }

        Decide(request, AccessRequestStatus.Rejected, reviewerUsername, note);
        await _requests.UpdateAsync(request, ct);
        await AuditAsync(AuditAction.AccessRequestRejected, request,
            $"Access to '{request.SecretName}' rejected for {request.RequesterUsername}", ct);
        await _notifications.NotifyAccessRequestDecidedAsync(
            request.RequesterUserId, request.SecretName, approved: false, reviewerUsername, note, ct);
        return true;
    }

    public async Task<bool> CancelAsync(Guid id, Guid requesterUserId, CancellationToken ct)
    {
        var request = await _requests.FindAsync(id, ct);
        if (request is null || request.RequesterUserId != requesterUserId || request.Status != AccessRequestStatus.Pending)
        {
            return false;
        }

        Decide(request, AccessRequestStatus.Cancelled, request.RequesterUsername, null);
        await _requests.UpdateAsync(request, ct);
        await AuditAsync(AuditAction.AccessRequestCancelled, request, $"Request for '{request.SecretName}' withdrawn", ct);
        return true;
    }

    private void Decide(AccessRequest request, AccessRequestStatus status, string by, string? note)
    {
        request.Status = status;
        request.DecidedAtUtc = _clock.GetUtcNow();
        request.DecidedBy = by;
        request.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    private Task AuditAsync(AuditAction action, AccessRequest request, string details, CancellationToken ct)
        => _audit.WriteAsync(new AuditEntry
        {
            Action = action,
            ResourceType = nameof(Secret),
            ResourceId = request.SecretId,
            ResourceName = request.SecretName,
            Details = details
        }, ct);

    private static AccessRequestDto Map(AccessRequest r) => new(
        r.Id, r.SecretId, r.SecretName, r.ProjectKey, r.RequesterUsername, r.Reason, r.DeniedReasons,
        r.Status, r.CreatedAtUtc, r.DecidedAtUtc, r.DecidedBy, r.DecisionNote);
}
