using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.AccessRequests;

public sealed class AccessRequestService : IAccessRequestService
{
    private const int MinReasonLength = 3;
    private const int MaxReasonLength = 500;

    public const int LongestExpiryDays = 3650;
    public static readonly string ExpiryOutOfRange = $"Grant expiry must be between 0 and {LongestExpiryDays} days.";

    private readonly IAccessRequestRepository _requests;
    private readonly ISecretGrantRepository _grants;
    private readonly ISecretRepository _secrets;
    private readonly IUserRepository _users;
    private readonly IAccessRequestCommitter _committer;
    private readonly IAuditContext _actor;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _clock;

    public AccessRequestService(
        IAccessRequestRepository requests, ISecretGrantRepository grants, ISecretRepository secrets,
        IUserRepository users, IAccessRequestCommitter committer, IAuditContext actor,
        INotificationService notifications, TimeProvider clock)
    {
        _requests = requests;
        _grants = grants;
        _secrets = secrets;
        _users = users;
        _committer = committer;
        _actor = actor;
        _notifications = notifications;
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

    public async Task<bool> ApproveAsync(Guid id, int expiresInDays, CancellationToken ct)
    {
        if (expiresInDays is < 0 or > LongestExpiryDays)
        {
            throw new SharingException(ExpiryOutOfRange);
        }

        var request = await _requests.FindAsync(id, ct);
        if (request is null || request.Status != AccessRequestStatus.Pending)
        {
            return false;
        }

        var secret = await _secrets.FindAsync(request.SecretId, ct) ?? throw new SecretNotFoundException(request.SecretId);
        var recipient = await _users.FindByIdAsync(request.RequesterUserId, ct)
            ?? throw new SharingException("That requester account no longer exists.");
        ShareRecipient.EnsureCanReceive(secret, recipient, _actor.UserId);

        var now = _clock.GetUtcNow();
        var existing = await _grants.FindForRecipientAsync(recipient.Id, secret.Id, ct);
        var needsShare = existing is null || !existing.IsActive(now);
        var share = needsShare ? ShareRecipient.NewShare(secret, recipient, _actor, now, expiresInDays) : null;

        Decide(request, AccessRequestStatus.Approved, _actor.Username ?? "system");
        if (!await _committer.ApproveAsync(request, share, needsShare ? existing : null, ct))
        {
            return false;
        }

        await _notifications.NotifyAccessRequestDecidedAsync(
            request.RequesterUserId, request.SecretName, approved: true, request.DecidedBy!, note: null, ct);
        return true;
    }

    public async Task<bool> RejectAsync(Guid id, CancellationToken ct)
    {
        var request = await _requests.FindAsync(id, ct);
        if (request is null || request.Status != AccessRequestStatus.Pending)
        {
            return false;
        }

        Decide(request, AccessRequestStatus.Rejected, _actor.Username ?? "system");
        if (!await _requests.UpdateAsync(request, ct))
        {
            return false;
        }

        await _notifications.NotifyAccessRequestDecidedAsync(
            request.RequesterUserId, request.SecretName, approved: false, request.DecidedBy!, note: null, ct);
        return true;
    }

    public async Task<bool> CancelAsync(Guid id, Guid requesterUserId, CancellationToken ct)
    {
        var request = await _requests.FindAsync(id, ct);
        if (request is null || request.RequesterUserId != requesterUserId || request.Status != AccessRequestStatus.Pending)
        {
            return false;
        }

        Decide(request, AccessRequestStatus.Cancelled, request.RequesterUsername);
        return await _requests.UpdateAsync(request, ct);
    }

    private void Decide(AccessRequest request, AccessRequestStatus status, string by)
    {
        request.Status = status;
        request.DecidedAtUtc = _clock.GetUtcNow();
        request.DecidedBy = by;
    }

    private static AccessRequestDto Map(AccessRequest r) => new(
        r.Id, r.SecretId, r.SecretName, r.ProjectKey, r.RequesterUsername, r.Reason, r.DeniedReasons,
        r.Status, r.CreatedAtUtc, r.DecidedAtUtc, r.DecidedBy, r.DecisionNote);
}
