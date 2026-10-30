using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class SecretGrantRepository : ISecretGrantRepository
{
    private const string OneSharePerRecipientIndex = "IX_SecretGrants_SecretId_GranteeUserId";

    private readonly EclipsVaultDbContext _context;

    public SecretGrantRepository(EclipsVaultDbContext context) => _context = context;

    public Task<bool> AddAsync(SecretGrant grant, CancellationToken ct)
    {
        _context.SecretGrants.Add(grant);
        return SaveNewShareAsync(grant, replacedShareId: null, ct);
    }

    public Task<bool> ReplaceExpiredAsync(SecretGrant expired, SecretGrant grant, CancellationToken ct)
    {
        _context.SecretGrants.Remove(expired);
        _context.SecretGrants.Add(grant);
        return SaveNewShareAsync(grant, expired.Id, ct);
    }

    public Task<SecretGrant?> FindAsync(Guid grantId, CancellationToken ct)
        => _context.SecretGrants.FirstOrDefaultAsync(g => g.Id == grantId, ct);

    public async Task<bool> RemoveAsync(Guid grantId, CancellationToken ct)
    {
        var grant = await _context.SecretGrants.FirstOrDefaultAsync(g => g.Id == grantId, ct);
        if (grant is null)
        {
            return false;
        }

        _context.SecretGrants.Remove(grant);
        try
        {
            await SaveShareChangeAsync(ct);
        }
        catch (AuditWriteFailedException failure) when (failure.InnerException is DbUpdateConcurrencyException)
        {
            // The failed transaction has rolled back. A competing deletion is a missing share,
            // not an unavailable audit store; every other save failure still fails closed.
            if (await _context.SecretGrants.AsNoTracking().AnyAsync(share => share.Id == grantId, ct))
            {
                throw;
            }

            return false;
        }

        return true;
    }

    private async Task<bool> SaveNewShareAsync(SecretGrant grant, Guid? replacedShareId, CancellationToken ct)
    {
        try
        {
            await SaveShareChangeAsync(ct);
        }
        catch (AuditWriteFailedException failure) when (failure.InnerException is DbUpdateException save && MayHaveLostToAnotherShare(save))
        {
            // The failed transaction has rolled back. Losing a race to another share for this
            // recipient is a conflict, not an unavailable audit store; every other save failure,
            // including one that leaves only the expired share, still fails closed.
            if (!await AnotherShareExistsAsync(grant, replacedShareId, ct))
            {
                throw;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// The one-share-per-recipient index refused the insert, or the expired share a replacement
    /// deletes was already gone. Either way another request may have saved a share first.
    /// </summary>
    private static bool MayHaveLostToAnotherShare(DbUpdateException failure)
        => failure is DbUpdateConcurrencyException || UniqueIndex.IsViolatedBy(failure, OneSharePerRecipientIndex);

    private async Task<bool> AnotherShareExistsAsync(SecretGrant grant, Guid? replacedShareId, CancellationToken ct)
    {
        try
        {
            return await _context.SecretGrants.AsNoTracking().AnyAsync(share => share.SecretId == grant.SecretId
                && share.GranteeUserId == grant.GranteeUserId
                && share.Id != replacedShareId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database that failed the save may still be unreachable. Nothing shows a race was
            // lost, so the save's own failure is the answer.
            return false;
        }
    }

    private async Task SaveShareChangeAsync(CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The interceptor includes the audit row in this transaction. A failed save must use
            // the same fail-closed response as a standalone audit write, after rollback completes.
            throw new AuditWriteFailedException("The share and its audit row could not be persisted.", ex);
        }
    }

    public Task<bool> HasActiveGrantAsync(Guid userId, Guid secretId, DateTimeOffset asOfUtc, CancellationToken ct)
        => _context.SecretGrants
            .AsNoTracking()
            .AnyAsync(g => g.GranteeUserId == userId
                           && g.SecretId == secretId
                           && (g.ExpiresAtUtc == null || g.ExpiresAtUtc > asOfUtc), ct);

    public Task<SecretGrant?> FindForRecipientAsync(Guid userId, Guid secretId, CancellationToken ct)
        => _context.SecretGrants.FirstOrDefaultAsync(g => g.GranteeUserId == userId && g.SecretId == secretId, ct);

    public async Task<IReadOnlyList<SecretGrant>> ListForSecretAsync(Guid secretId, CancellationToken ct)
        => await _context.SecretGrants
            .AsNoTracking()
            .Where(g => g.SecretId == secretId)
            .OrderBy(g => g.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SharedSecretDto>> ListSharedWithUserAsync(Guid userId, DateTimeOffset asOfUtc, CancellationToken ct)
        => await (
            from g in _context.SecretGrants.AsNoTracking()
            join s in _context.Secrets.AsNoTracking() on g.SecretId equals s.Id
            where g.GranteeUserId == userId
                  && (g.ExpiresAtUtc == null || g.ExpiresAtUtc > asOfUtc)
                  && !s.IsShredded
                  && (s.ExpiresAtUtc == null || s.ExpiresAtUtc > asOfUtc)
            orderby s.Name
            select new SharedSecretDto(s.Id, s.Name, s.ProjectKey, s.Environment, s.Sensitivity, g.GrantedBy, g.ExpiresAtUtc))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OutgoingShareDto>> ListIssuedByAsync(Guid issuerUserId, DateTimeOffset asOfUtc, CancellationToken ct)
        => await (
            from g in _context.SecretGrants.AsNoTracking()
            join s in _context.Secrets.AsNoTracking() on g.SecretId equals s.Id
            where g.IssuerUserId == issuerUserId
                  && (g.ExpiresAtUtc == null || g.ExpiresAtUtc > asOfUtc)
                  && !s.IsShredded
            orderby g.CreatedAtUtc descending
            select new OutgoingShareDto(g.Id, s.Id, s.Name, g.GranteeUsername, g.CreatedAtUtc, g.ExpiresAtUtc))
            .ToListAsync(ct);
}
