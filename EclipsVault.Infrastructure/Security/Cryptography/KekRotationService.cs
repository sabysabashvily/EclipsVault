using System.Security.Cryptography;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using EclipsVault.Infrastructure.Persistence.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// Re-wraps the data keys of secrets and archived versions under the current master key, in batches
/// (ADR-0029). Only the wrapped data key and master key id change: the payload is untouched, so a pass
/// never decrypts a value, a decoy's included. Each batch commits with its critical
/// <c>KekRotated</c> events, so other audited actions wait for the audit chain one batch at a time.
/// </summary>
public sealed class KekRotationService : IKekRotationService
{
    private const int BatchSize = 500;
    private const string PassLockResource = "EclipsVault:KekRotation";

    private readonly EclipsVaultDbContext _db;
    private readonly ICryptoEngineFactory _cryptoFactory;
    private readonly ISecretCache _cache;
    private readonly AuditChain _chain;
    private readonly AuditSaveChangesInterceptor _audit;
    private readonly ISessionLocker _locks;

    public KekRotationService(
        EclipsVaultDbContext db,
        ICryptoEngineFactory cryptoFactory,
        ISecretCache cache,
        AuditChain chain,
        AuditSaveChangesInterceptor audit,
        ISessionLocker locks)
    {
        _db = db;
        _cryptoFactory = cryptoFactory;
        _cache = cache;
        _chain = chain;
        _audit = audit;
        _locks = locks;
    }

    public async Task<KekStatus> GetStatusAsync(CancellationToken ct)
    {
        var keys = await _cryptoFactory.Create().GetMasterKeysAsync(ct);
        var current = keys.CurrentKekId;
        var known = keys.KnownKekIds;

        var secretGroups = await _db.Secrets.AsNoTracking()
            .Where(s => !s.IsShredded)
            .GroupBy(s => s.KekId)
            .Select(g => new { KekId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var versionGroups = await _db.SecretVersions.AsNoTracking()
            .GroupBy(v => v.KekId)
            .Select(g => new { KekId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var kekIds = secretGroups.Select(g => g.KekId)
            .Concat(versionGroups.Select(g => g.KekId))
            .Concat(known)
            .Distinct(StringComparer.Ordinal);

        var usage = kekIds
            .Select(id => new KekUsage(
                id,
                IsCurrent: id == current,
                IsKnown: known.Contains(id),
                SecretCount: secretGroups.FirstOrDefault(g => g.KekId == id)?.Count ?? 0,
                VersionCount: versionGroups.FirstOrDefault(g => g.KekId == id)?.Count ?? 0))
            .OrderByDescending(u => u.IsCurrent)
            .ThenByDescending(u => u.Total)
            .ThenBy(u => u.KekId, StringComparer.Ordinal)
            .ToList();

        var notCurrent = usage.Where(u => !u.IsCurrent).Sum(u => u.Total);
        var onMissingKeys = usage.Where(u => !u.IsKnown).Sum(u => u.Total);
        return new KekStatus(current, known, usage, AwaitingRotation: notCurrent - onMissingKeys, onMissingKeys, keys.KeyService);
    }

    public async Task<KekRotationResult> RotateAsync(CancellationToken ct)
    {
        // Owned by the database session, so it holds across every batch transaction, and the
        // database releases it if this process dies mid-pass.
        await using var pass = await _locks.TryAcquireAsync(_db, PassLockResource, ct)
            ?? throw new KekRotationRunningException();

        var engine = _cryptoFactory.Create();
        var keys = await engine.GetMasterKeysAsync(ct);
        var retired = keys.KnownKekIds.Where(id => id != keys.CurrentKekId).ToList();

        var secrets = 0;
        var versions = 0;
        try
        {
            await RotateRowsAsync(
                _db.Secrets.AsNoTracking()
                    .Where(s => !s.IsShredded && retired.Contains(s.KekId))
                    .Select(s => new ReadEnvelope { Id = s.Id, Ciphertext = s.Ciphertext, WrappedDek = s.WrappedDek, KekId = s.KekId, Algorithm = s.Algorithm }),
                areSecrets: true, engine, committed => secrets += committed, ct);

            await RotateRowsAsync(
                _db.SecretVersions.AsNoTracking()
                    .Where(v => retired.Contains(v.KekId))
                    .Select(v => new ReadEnvelope { Id = v.Id, Ciphertext = v.Ciphertext, WrappedDek = v.WrappedDek, KekId = v.KekId, Algorithm = v.Algorithm }),
                areSecrets: false, engine, committed => versions += committed, ct);

            // Read again: a key service may have moved to a newer key version during the pass, and
            // rows re-wrapped onto it are not on a missing key.
            var after = await engine.GetMasterKeysAsync(ct);
            var known = after.KnownKekIds.ToList();
            var onMissingKeys = await _db.Secrets.CountAsync(s => !s.IsShredded && !known.Contains(s.KekId), ct)
                + await _db.SecretVersions.CountAsync(v => !known.Contains(v.KekId), ct);

            return new KekRotationResult(after.CurrentKekId, secrets, versions, onMissingKeys);
        }
        catch (AuditWriteFailedException unrecorded)
        {
            throw new KekRotationStoppedException(secrets, versions, unrecorded);
        }
        catch (Exception failure) when (failure is not OperationCanceledException && secrets + versions > 0)
        {
            throw new KekRotationInterruptedException(secrets, versions, failure);
        }
    }

    /// <summary>
    /// Reports each batch's rows to <paramref name="onCommitted"/> as it commits, so a pass stopped by a
    /// later batch, or by clearing the cache after this one, still knows what committed.
    /// </summary>
    private async Task RotateRowsAsync(
        IQueryable<ReadEnvelope> pending, bool areSecrets, ICryptoEngine engine, Action<int> onCommitted, CancellationToken ct)
    {
        Guid? after = null;
        while (true)
        {
            // Checked only here, between batches, so the updates and their audit events commit together.
            ct.ThrowIfCancellationRequested();

            var batch = await pending
                .Where(row => after == null || row.Id > after.Value)
                .OrderBy(row => row.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
            {
                return;
            }

            await CommitBatchAsync(batch, areSecrets, engine, onCommitted);

            // Past every row the batch read, moved or skipped, so a row that keeps changing cannot
            // hold the pass in place.
            after = batch[^1].Id;
        }
    }

    /// <summary>
    /// Re-wraps each row the batch read, but writes it only if its wrapped data key and master key id
    /// are still what was read; a row changed, deleted or shredded since is left alone, and counts as
    /// not moved. A changed value is already sealed under the current master key with a new data key.
    /// A row whose re-wrap changes nothing, or whose data key does not open, is not written either.
    /// </summary>
    private async Task CommitBatchAsync(
        IReadOnlyList<ReadEnvelope> batch, bool areSecrets, ICryptoEngine engine, Action<int> onCommitted)
    {
        var rewraps = new List<(ReadEnvelope Read, SealedSecret Rewrapped)>(batch.Count);
        foreach (var row in batch)
        {
            SealedSecret rewrapped;
            try
            {
                rewrapped = await engine.RewrapAsync(row.ToSealedSecret(), CancellationToken.None);
            }
            catch (CryptographicException)
            {
                // A data key that no longer opens under its own master key stays pending, as a row
                // under a missing key does, so one damaged row cannot stop every pass at itself.
                continue;
            }

            // An engine may hand back a row already under its current key unchanged; writing it would
            // record a re-wrap that never happened.
            if (rewrapped.KekId != row.KekId || !rewrapped.WrappedDek.AsSpan().SequenceEqual(row.WrappedDek))
            {
                rewraps.Add((row, rewrapped));
            }
        }

        if (rewraps.Count == 0)
        {
            return;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(CancellationToken.None);
        await TakeAuditChainAsync();

        var moved = new List<(ReadEnvelope Read, SealedSecret Rewrapped)>();
        foreach (var rewrap in rewraps)
        {
            if (await RewrapIfUnchangedAsync(rewrap.Read, rewrap.Rewrapped, areSecrets) == 1)
            {
                moved.Add(rewrap);
            }
        }

        if (moved.Count == 0)
        {
            return;
        }

        // A pair of stored envelope ids fits in one audit row even when a batch spans many keys.
        var events = new List<AuditEntry>();
        foreach (var (keys, count) in moved.CountBy(row => (From: row.Read.KekId, To: row.Rewrapped.KekId)))
        {
            var secrets = areSecrets ? count : 0;
            var versions = areSecrets ? 0 : count;
            events.Add(new AuditEntry
            {
                Action = AuditAction.KekRotated,
                ResourceType = "Kek",
                ResourceName = keys.To,
                Details = $"Re-wrapped {secrets} secret(s) and {versions} archived version(s) from KEK {keys.From} to KEK {keys.To}",
                IsCritical = true
            });
        }
        await RecordAndCommitAsync(transaction, events);
        onCommitted(moved.Count);

        // Archived versions are never cached; only a secret's own envelope is.
        if (areSecrets)
        {
            foreach (var row in moved)
            {
                await _cache.EvictAsync(row.Read.Id, CancellationToken.None);
            }
        }
    }

    private Task<int> RewrapIfUnchangedAsync(ReadEnvelope read, SealedSecret rewrapped, bool isSecret)
        => isSecret
            ? _db.Secrets
                .Where(s => s.Id == read.Id && !s.IsShredded && s.WrappedDek == read.WrappedDek && s.KekId == read.KekId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.WrappedDek, rewrapped.WrappedDek)
                    .SetProperty(s => s.KekId, rewrapped.KekId), CancellationToken.None)
            : _db.SecretVersions
                .Where(v => v.Id == read.Id && v.WrappedDek == read.WrappedDek && v.KekId == read.KekId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(v => v.WrappedDek, rewrapped.WrappedDek)
                    .SetProperty(v => v.KekId, rewrapped.KekId), CancellationToken.None);

    /// <summary>
    /// Taken before the batch writes any row, as every audited writer takes it, so the batch cannot
    /// hold a row another writer needs while waiting for the chain that writer holds.
    /// </summary>
    private async Task TakeAuditChainAsync()
    {
        try
        {
            await _chain.AcquireAsync(_db, CancellationToken.None);
        }
        catch (Exception failure)
        {
            throw new AuditWriteFailedException("The audit chain could not be taken, so the rotation stopped before this batch.", failure);
        }
    }

    private async Task RecordAndCommitAsync(IDbContextTransaction transaction, IReadOnlyList<AuditEntry> events)
    {
        try
        {
            await _audit.SaveOperationAsync(_db, events, CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch (Exception failure)
        {
            throw new AuditWriteFailedException("The rotation batch could not be stored with its audit events, so the pass stopped.", failure);
        }
    }

    /// <summary>A row's envelope as the pass read it, which a write must still find in place.</summary>
    private sealed class ReadEnvelope : IEnvelope
    {
        public Guid Id { get; init; }

        public byte[] Ciphertext { get; init; } = [];

        public byte[] WrappedDek { get; init; } = [];

        public string KekId { get; init; } = string.Empty;

        public string Algorithm { get; init; } = string.Empty;
    }
}
