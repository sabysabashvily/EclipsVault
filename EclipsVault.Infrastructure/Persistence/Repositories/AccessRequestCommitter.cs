using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class AccessRequestCommitter(EclipsVaultDbContext context) : IAccessRequestCommitter
{
    public Task<bool> ApproveAsync(AccessRequest request, SecretGrant share, SecretGrant? expiredShare, CancellationToken ct)
    {
        StageDecision(request);
        if (expiredShare is not null)
        {
            context.SecretGrants.Remove(expiredShare);
        }
        context.SecretGrants.Add(share);

        return SaveAsync(request, share, expiredShare?.Id, ct);
    }

    internal Task<bool> DecideAsync(AccessRequest request, CancellationToken ct)
    {
        StageDecision(request);
        return SaveAsync(request, share: null, replacedShareId: null, ct);
    }

    private void StageDecision(AccessRequest request)
    {
        // Status is the concurrency token. Pinning its original value to Pending makes the save fail
        // when another decision committed first, even if this entity was read before that one.
        var decision = context.Entry(request);
        decision.Property(r => r.Status).OriginalValue = AccessRequestStatus.Pending;
        decision.Property(r => r.Status).IsModified = true;
        decision.Property(r => r.DecidedBy).IsModified = true;
        decision.Property(r => r.DecidedAtUtc).IsModified = true;
    }

    private async Task<bool> SaveAsync(AccessRequest request, SecretGrant? share, Guid? replacedShareId, CancellationToken ct)
    {
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (MayHaveLostRace(ex, request.Id, replacedShareId))
            {
                // SaveChanges has rolled back and discarded staged changes. Check the decision first:
                // two approvals can hit the share index before EF reaches the request's status update.
                var decided = await IsDecidedAsync(request.Id, ct);
                if (decided is true)
                {
                    return false;
                }
                if (decided is false && share is not null
                    && await OneSharePerRecipient.AnotherShareExistsAsync(context, share, replacedShareId, ct))
                {
                    throw new SharingException("They already have a share on this secret.");
                }
            }

            throw new AuditWriteFailedException("The access request and its audit rows could not be persisted.", ex);
        }
    }

    private static bool MayHaveLostRace(Exception failure, Guid requestId, Guid? replacedShareId)
        => failure is DbUpdateConcurrencyException conflict
            ? conflict.Entries.Any(entry => entry.Entity is AccessRequest r && r.Id == requestId
                || entry.Entity is SecretGrant share && share.Id == replacedShareId)
            : failure is DbUpdateException update && UniqueIndex.IsViolatedBy(update, OneSharePerRecipient.Index);

    /// <summary>True when the request is no longer pending or is gone; null when the store cannot be read.</summary>
    private async Task<bool?> IsDecidedAsync(Guid requestId, CancellationToken ct)
    {
        try
        {
            var status = await context.AccessRequests.AsNoTracking().Where(r => r.Id == requestId)
                .Select(r => (AccessRequestStatus?)r.Status).SingleOrDefaultAsync(ct);
            return status is null || status != AccessRequestStatus.Pending;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
