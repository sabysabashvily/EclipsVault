using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class AccessRequestCommitter(EclipsVaultDbContext context) : IAccessRequestCommitter
{
    private const string OneSharePerRecipientIndex = "IX_SecretGrants_SecretId_GranteeUserId";

    public Task<bool> ApproveAsync(AccessRequest request, SecretGrant? share, SecretGrant? expiredShare, CancellationToken ct)
        => SaveDecisionAsync(request, share, expiredShare, ct);

    internal async Task<bool> SaveDecisionAsync(AccessRequest request, SecretGrant? share, SecretGrant? expiredShare, CancellationToken ct)
    {
        var replacedShareId = expiredShare?.Id;
        var decision = context.Entry(request);
        decision.Property(r => r.Status).OriginalValue = AccessRequestStatus.Pending;
        decision.Property(r => r.Status).IsModified = true;
        decision.Property(r => r.DecidedBy).IsModified = true;
        decision.Property(r => r.DecidedAtUtc).IsModified = true;
        if (expiredShare is not null)
        {
            context.SecretGrants.Remove(expiredShare);
        }
        if (share is not null)
        {
            context.SecretGrants.Add(share);
        }

        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failure = new AuditWriteFailedException("The access request and its audit rows could not be persisted.", ex);
            if (!MayHaveLostRace(ex, request.Id, expiredShare?.Id))
            {
                throw failure;
            }

            // SaveChanges has rolled back and discarded staged changes. Check the decision first:
            // two approvals can hit the share index before EF reaches the request's status update.
            try
            {
                var status = await context.AccessRequests.AsNoTracking().Where(r => r.Id == request.Id)
                    .Select(r => (AccessRequestStatus?)r.Status).SingleOrDefaultAsync(ct);
                if (status is null || status != AccessRequestStatus.Pending)
                {
                    return false;
                }

                if (share is not null && await context.SecretGrants.AsNoTracking().AnyAsync(s =>
                    s.SecretId == share.SecretId && s.GranteeUserId == share.GranteeUserId && s.Id != replacedShareId, ct))
                {
                    throw new SharingException("They already have a share on this secret.");
                }
            }
            catch (SharingException)
            {
                throw;
            }
            catch (Exception lookup) when (lookup is not OperationCanceledException)
            {
                // An unreachable store cannot prove a race; keep the original save failure.
                throw failure;
            }

            throw failure;
        }
    }

    private static bool MayHaveLostRace(Exception failure, Guid requestId, Guid? expiredShareId)
        => failure is DbUpdateConcurrencyException conflict
            ? conflict.Entries.Any(entry => entry.Entity is AccessRequest r && r.Id == requestId
                || entry.Entity is SecretGrant share && share.Id == expiredShareId)
            : failure is DbUpdateException update && UniqueIndex.IsViolatedBy(update, OneSharePerRecipientIndex);
}
