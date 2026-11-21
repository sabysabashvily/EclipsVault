using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class SecretRepository : ISecretRepository
{
    internal const string UniqueNameIndex = "IX_Secrets_Name";

    private readonly EclipsVaultDbContext _context;
    private readonly AuditSaveChangesInterceptor _audit;
    private readonly AuditChain _chain;

    public SecretRepository(EclipsVaultDbContext context, AuditSaveChangesInterceptor audit, AuditChain chain)
    {
        _context = context;
        _audit = audit;
        _chain = chain;
    }

    public Task<Secret?> FindAsync(Guid id, CancellationToken ct)
        => _context.Secrets.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Secret>> ListActiveAsync(DateTimeOffset asOfUtc, CancellationToken ct)
        => await _context.Secrets
            .AsNoTracking()
            .Where(s => !s.IsShredded && (s.ExpiresAtUtc == null || s.ExpiresAtUtc > asOfUtc))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Secret>> ListExpiredAsync(DateTimeOffset asOfUtc, CancellationToken ct)
        => await _context.Secrets
            .Where(s => !s.IsShredded && s.ExpiresAtUtc != null && s.ExpiresAtUtc <= asOfUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Secret>> ListExpiringAsync(DateTimeOffset asOfUtc, DateTimeOffset horizonUtc, CancellationToken ct)
        => await _context.Secrets
            .Where(s => !s.IsShredded
                        && s.ExpiresAtUtc != null
                        && s.ExpiresAtUtc > asOfUtc
                        && s.ExpiresAtUtc <= horizonUtc
                        && (s.ExpiryNoticeSentForUtc == null || s.ExpiryNoticeSentForUtc != s.ExpiresAtUtc))
            .OrderBy(s => s.ExpiresAtUtc)
            .ToListAsync(ct);

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct)
        => _context.Secrets.AnyAsync(s => s.Name == name, ct);

    public async Task AddAsync(Secret secret, CancellationToken ct)
    {
        _context.Secrets.Add(secret);
        // The interceptor saves the audit row in the same transaction, so any other failed save is the
        // fail-closed audit failure. Losing the unique name to another request is a conflict instead.
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException failure) when (UniqueIndex.IsViolatedBy(failure, UniqueNameIndex))
        {
            throw new SecretNameTakenException();
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new AuditWriteFailedException("The secret and its audit row could not be stored.", failure);
        }
    }

    public async Task UpdateAsync(Secret secret, CancellationToken ct)
    {
        _context.Secrets.Update(secret);
        await _context.SaveChangesAsync(ct);
    }

    public async Task MarkExpiryNoticeSentAsync(Secret secret, CancellationToken ct)
    {
        // Mark the single column rather than the whole entity: DbSet.Update() flags every property
        // as modified, which the audit interceptor cannot tell apart from a genuine edit.
        _context.Entry(secret).Property(s => s.ExpiryNoticeSentForUtc).IsModified = true;
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Secret secret, CancellationToken ct)
    {
        _context.Secrets.Remove(secret); // archived versions cascade-delete via the FK
        // The interceptor saves the audit row in the same transaction, so a failed save is the fail-closed audit failure.
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new AuditWriteFailedException("The secret's deletion and its audit row could not be stored.", failure);
        }
    }

    public async Task RotateAsync(
        Secret secret, byte[] readWrappedDek, SecretVersion archivedVersion, AuditAction recordedAs, CancellationToken ct)
    {
        var recorded = new AuditEntry { Action = recordedAs, ResourceType = nameof(Secret), ResourceId = secret.Id, ResourceName = secret.Name };
        try
        {
            await using var transaction = await BeginUnlessInOneAsync(ct);
            await _chain.AcquireAsync(_context, ct);
            if (await ReadUnderChainAsync(secret.Id, ct) is not { IsShredded: false } current
                || !current.WrappedDek.AsSpan().SequenceEqual(readWrappedDek))
            {
                throw new SecretChangedException();
            }

            archivedVersion.VersionNumber = await _context.SecretVersions
                .Where(v => v.SecretId == secret.Id).MaxAsync(v => (int?)v.VersionNumber, ct) + 1 ?? 1;
            _context.SecretVersions.Add(archivedVersion);
            _context.Secrets.Update(secret);
            await _audit.SaveOperationAsync(_context, [recorded], ct);
            await (transaction?.CommitAsync(ct) ?? Task.CompletedTask);
        }
        catch (Exception failure)
        {
            KeepAsStored(secret);
            if (failure is SecretChangedException or OperationCanceledException)
            {
                throw;
            }

            throw new AuditWriteFailedException("The secret's new value and its audit row could not be stored.", failure);
        }
    }

    public async Task<bool> ShredAsync(Secret secret, byte[] readWrappedDek, DateTimeOffset readExpiresAtUtc, CancellationToken ct)
    {
        try
        {
            await using var transaction = await BeginUnlessInOneAsync(ct);
            await _chain.AcquireAsync(_context, ct);
            if (await ReadUnderChainAsync(secret.Id, ct) is not { IsShredded: false } current
                || !current.WrappedDek.AsSpan().SequenceEqual(readWrappedDek)
                || current.ExpiresAtUtc != readExpiresAtUtc)
            {
                KeepAsStored(secret);
                return false;
            }

            // Archived versions hold key material — purge them as part of the shred. Materialised rather
            // than handed to RemoveRange as a query, which would enumerate it with a blocking call.
            var versions = await _context.SecretVersions.Where(v => v.SecretId == secret.Id).ToListAsync(ct);
            _context.SecretVersions.RemoveRange(versions);
            _context.Secrets.Update(secret);
            await _context.SaveChangesAsync(ct); // interceptor injects the SecretShredded audit
            await (transaction?.CommitAsync(ct) ?? Task.CompletedTask);
            return true;
        }
        catch
        {
            KeepAsStored(secret);
            throw;
        }
    }

    public Task ReloadAsync(Secret secret, CancellationToken ct) => _context.Entry(secret).ReloadAsync(ct);

    /// <summary>
    /// A transaction of this write's own, or none when the caller already opened one, which then ends it.
    /// </summary>
    private async Task<IDbContextTransaction?> BeginUnlessInOneAsync(CancellationToken ct)
        => _context.Database.CurrentTransaction is null ? await _context.Database.BeginTransactionAsync(ct) : null;

    /// <summary>
    /// The row as committed, read while holding the audit chain. Every write to a secret's value is audited
    /// and so takes the chain before it writes, so none can commit between this read and this write.
    /// </summary>
    private Task<StoredValue?> ReadUnderChainAsync(Guid secretId, CancellationToken ct)
        => _context.Secrets.AsNoTracking()
            .Where(s => s.Id == secretId)
            .Select(s => new StoredValue(s.WrappedDek, s.IsShredded, s.ExpiresAtUtc))
            .SingleOrDefaultAsync(ct);

    /// <summary>Puts the caller's entity back to what is stored, as every write here does when it writes nothing.</summary>
    private void KeepAsStored(Secret secret)
    {
        var entry = _context.Entry(secret);
        if (entry.State is EntityState.Detached or EntityState.Added)
        {
            return;
        }

        entry.CurrentValues.SetValues(entry.OriginalValues);
        entry.State = EntityState.Unchanged;
    }

    public async Task<IReadOnlyList<SecretVersion>> ListVersionsAsync(Guid secretId, CancellationToken ct)
        => await _context.SecretVersions
            .AsNoTracking()
            .Where(v => v.SecretId == secretId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);

    public Task<SecretVersion?> FindVersionAsync(Guid secretId, Guid versionId, CancellationToken ct)
        => _context.SecretVersions.FirstOrDefaultAsync(v => v.Id == versionId && v.SecretId == secretId, ct);

    private sealed record StoredValue(byte[] WrappedDek, bool IsShredded, DateTimeOffset? ExpiresAtUtc);
}
