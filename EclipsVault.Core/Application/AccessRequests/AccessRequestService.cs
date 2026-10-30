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
    private readonly IIntrusionResponseService _intrusion;
    private readonly IAuditContext _actor;
    private readonly INotificationService _notifications;
    private readonly TimeProvider _clock;

    public AccessRequestService(
        IAccessRequestRepository requests, ISecretGrantRepository grants, ISecretRepository secrets,
        IUserRepository users, IAccessRequestCommitter committer, IIntrusionResponseService intrusion, IAuditContext actor,
        INotificationService notifications, TimeProvider clock)
    {
        _requests = requests;
        _grants = grants;
        _secrets = secrets;
        _users = users;
        _committer = committer;
        _intrusion = intrusion;
        _actor = actor;
        _notifications = notifications;
        _clock = clock;
    }

    public async Task<AccessRequestRefusal?> FilingRefusalAsync(Guid secretId, Guid requesterUserId, CancellationToken ct)
        => await FilingRefusalAsync(await LiveSecretAsync(secretId, ct), requesterUserId, ct);

    public async Task<SecretSummaryDto> SecretToRequestAsync(Guid secretId, CancellationToken ct)
    {
        var secret = await LiveSecretAsync(secretId, ct);
        return new SecretSummaryDto(
            secret.Id, secret.Name, secret.ProjectKey, secret.Environment, secret.Sensitivity, secret.CreatedAtUtc, secret.ExpiresAtUtc);
    }

    public async Task<AccessRequestCreateResult> CreateAsync(
        Guid secretId, Guid requesterUserId, string requesterUsername, string reason,
        IReadOnlyList<string> denialReasons, CancellationToken ct)
    {
        var secret = await LiveSecretAsync(secretId, ct);
        if (secret.IsHoneyToken)
        {
            // Deliberately before any rule: the trap fires no matter who asks or why.
            await _intrusion.TriggerHoneyTokenAsync(secret.Id, secret.Name, ct);
            throw new HoneyTokenTrippedException(secret.Id, secret.Name);
        }

        if (await FilingRefusalAsync(secret, requesterUserId, ct) is { } refusal)
        {
            return AccessRequestCreateResult.Failed(AccessRequestRules.ToRequester(refusal));
        }

        reason = reason.Trim();
        if (reason.Length < MinReasonLength)
        {
            return AccessRequestCreateResult.Failed("Add a short reason so a reviewer can decide.");
        }

        if (reason.Length > MaxReasonLength)
        {
            reason = reason[..MaxReasonLength];
        }

        var request = new AccessRequest
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            SecretName = secret.Name,
            ProjectKey = secret.ProjectKey,
            RequesterUserId = requesterUserId,
            RequesterUsername = requesterUsername,
            Reason = reason,
            DeniedReasons = denialReasons.Count == 0 ? null : string.Join('\n', denialReasons),
            Status = AccessRequestStatus.Pending,
            CreatedAtUtc = _clock.GetUtcNow()
        };

        await _requests.AddAsync(request, ct);
        return AccessRequestCreateResult.Ok;
    }

    public async Task<IReadOnlyList<AccessRequestDto>> ListMineAsync(Guid requesterUserId, CancellationToken ct)
        => (await _requests.ListByRequesterAsync(requesterUserId, ct)).Select(AccessRequestDto.From).ToList();

    public Task<IReadOnlyList<AccessRequestForReview>> ListPendingForReviewAsync(CancellationToken ct)
        => _requests.ListPendingForReviewAsync(ct);

    public Task<AccessRequestForReview?> FindForReviewAsync(Guid id, CancellationToken ct)
        => _requests.FindForReviewAsync(id, ct);

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
        var now = _clock.GetUtcNow();
        var existing = await _grants.FindForRecipientAsync(recipient.Id, secret.Id, ct);
        if (AccessRequestRules.ShareRefusal(SubjectOf(recipient), AttributesOf(secret), existing?.IsActive(now) == true) is { } refusal)
        {
            throw new AccessRequestIneligibleException(refusal, AccessRequestRules.ToReviewer(refusal));
        }

        ShareRecipient.EnsureCanReceive(secret, recipient, _actor.UserId);
        var share = ShareRecipient.NewShare(secret, recipient, _actor, now, expiresInDays);

        Decide(request, AccessRequestStatus.Approved, _actor.Username ?? "system");
        if (!await _committer.ApproveAsync(request, share, expiredShare: existing, ct))
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

    /// <summary>A secret anyone could still open: not shredded and not expired.</summary>
    private async Task<Secret> LiveSecretAsync(Guid secretId, CancellationToken ct)
    {
        var secret = await _secrets.FindAsync(secretId, ct);
        return secret is null || secret.IsShredded || secret.ExpiresAtUtc <= _clock.GetUtcNow()
            ? throw new SecretNotFoundException(secretId)
            : secret;
    }

    private async Task<AccessRequestRefusal?> FilingRefusalAsync(Secret secret, Guid requesterUserId, CancellationToken ct)
    {
        var requester = await _users.FindByIdAsync(requesterUserId, ct)
            ?? throw new InvalidOperationException("The signed-in account no longer exists.");
        var holdsActiveShare = await _grants.HasActiveGrantAsync(requester.Id, secret.Id, _clock.GetUtcNow(), ct);
        if (AccessRequestRules.ShareRefusal(SubjectOf(requester), AttributesOf(secret), holdsActiveShare) is { } refusal)
        {
            return refusal;
        }

        return await _requests.HasPendingAsync(secret.Id, requester.Id, ct) ? AccessRequestRefusal.AlreadyPending : null;
    }

    private static SubjectAttributes SubjectOf(User account) => new(account.Clearance, account.ProjectKey);

    private static ResourceAttributes AttributesOf(Secret secret) => new(secret.Environment, secret.Sensitivity, secret.ProjectKey);
}
