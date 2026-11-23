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
/// never decrypts a value, a decoy's included. Each batch commits with its own critical
/// <c>KekRotated</c> row, so other audited actions wait for the audit chain one batch at a time.
/// </summary>
public sealed class KekRotationService : IKekRotationService
{
    private const int BatchSize = 500;
    private const string PassLockResource = "EclipsVault:KekRotation";

    private readonly EclipsVaultDbContext _db;
    private readonly ICryptoEngineFactory _cryptoFactory;
    private readonly IKekProvider _kek;
    private readonly ISecretCache _cache;
    private readonly AuditChain _chain;
    private readonly AuditSaveChangesInterceptor _audit;
    private readonly ISessionLocker _locks;

    public KekRotationService(
        EclipsVaultDbContext db,
        ICryptoEngineFactory cryptoFactory,
        IKekProvider kek,
        ISecretCache cache,
        AuditChain chain,
        AuditSaveChangesInterceptor audit,
        ISessionLocker locks)
    {
        _db = db;
        _cryptoFactory = cryptoFactory;
        _kek = kek;
        _cache = cache;
        _chain = chain;
        _audit = audit;
        _locks = locks;
    }

    public async Task<KekStatus> GetStatusAsync(CancellationToken ct)
    {
        var current = _kek.CurrentKekId;
        var known = _kek.KnownKekIds;

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
        var onMissingKeys = KeysAreLoadedHere(_cryptoFactory.Create()) ? usage.Where(u => !u.IsKnown).Sum(u => u.Total) : 0;
        return new KekStatus(current, known, usage, AwaitingRotation: notCurrent - onMissingKeys, onMissingKeys);
    }

    /// <summary>
    /// The KMS engine's key ids name its own key versions, not master keys loaded here, so under it
    /// every row not under the current key goes to its re-wrap, which knows which versions it holds.
    /// </summary>
    private static bool KeysAreLoadedHere(ICryptoEngine engine) => engine.EngineId == AesGcmCryptoEngine.EngineName;

    public async Task<KekRotationResult> RotateAsync(CancellationToken ct)
    {
        // Owned by the database session, so it holds across every batch transaction, and the
        // database releases it if this process dies mid-pass.
        await using var pass = await _locks.TryAcquireAsync(_db, PassLockResource, ct)
            ?? throw new KekRotationRunningException();

        var current = _kek.CurrentKekId;
        var known = _kek.KnownKekIds.ToList();
        var retired = known.Where(id => id != current).ToList();
        var engine = _cryptoFactory.Create();
        var keysAreLoadedHere = KeysAreLoadedHere(engine);

        var secrets = 0;
        var versions = 0;
        try
        {
            await RotateRowsAsync(
                _db.Secrets.AsNoTracking()
                    .Where(s => !s.IsShredded && s.KekId != current && (!keysAreLoadedHere || retired.Contains(s.KekId)))
                    .Select(s => new ReadEnvelope { Id = s.Id, Ciphertext = s.Ciphertext, WrappedDek = s.WrappedDek, KekId = s.KekId, Algorithm = s.Algorithm }),
                areSecrets: true, engine, committed => secrets += committed, ct);

            await RotateRowsAsync(
                _db.SecretVersions.AsNoTracking()
                    .Where(v => v.KekId != current && (!keysAreLoadedHere || retired.Contains(v.KekId)))
                    .Select(v => new ReadEnvelope { Id = v.Id, Ciphertext = v.Ciphertext, WrappedDek = v.WrappedDek, KekId = v.KekId, Algorithm = v.Algorithm }),
                areSecrets: false, engine, committed => versions += committed, ct);
        }
        catch (AuditWriteFailedException unrecorded)
        {
            throw new KekRotationStoppedException(secrets, versions, unrecorded);
        }

        var onMissingKeys = keysAreLoadedHere
            ? await _db.Secrets.CountAsync(s => !s.IsShredded && !known.Contains(s.KekId), ct)
                + await _db.SecretVersions.CountAsync(v => !known.Contains(v.KekId), ct)
            : 0;

        return new KekRotationResult(current, secrets, versions, onMissingKeys);
    }

    /// <summary>
    /// Reports each batch's rows to <paramref name="onCommitted"/> as it commits, so a pass stopped by a
    /// later batch still knows what the earlier ones moved.
    /// </summary>
    private async Task RotateRowsAsync(
        IQueryable<ReadEnvelope> pending, bool areSecrets, ICryptoEngine engine, Action<int> onCommitted, CancellationToken ct)
    {
        Guid? after = null;
        while (true)
        {
            // Checked only here, between batches: a batch either commits with its audit row or not at all.
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

            onCommitted(await CommitBatchAsync(batch, areSecrets, engine));

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
    private async Task<int> CommitBatchAsync(IReadOnlyList<ReadEnvelope> batch, bool areSecrets, ICryptoEngine engine)
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

            // The KMS engine returns a row already on its latest key version unchanged; writing it
            // would record a re-wrap that never happened.
            if (rewrapped.KekId != row.KekId || !rewrapped.WrappedDek.AsSpan().SequenceEqual(row.WrappedDek))
            {
                rewraps.Add((row, rewrapped));
            }
        }

        if (rewraps.Count == 0)
        {
            return 0;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(CancellationToken.None);
        await TakeAuditChainAsync();

        var moved = new List<ReadEnvelope>();
        foreach (var (read, rewrapped) in rewraps)
        {
            if (await RewrapIfUnchangedAsync(read, rewrapped, areSecrets) == 1)
            {
                moved.Add(read);
            }
        }

        if (moved.Count == 0)
        {
            return 0;
        }

        var secrets = areSecrets ? moved.Count : 0;
        var versions = areSecrets ? 0 : moved.Count;
        var retiredIds = string.Join(", ", moved.Select(row => row.KekId).Distinct().Order(StringComparer.Ordinal));
        await RecordAndCommitAsync(transaction, new AuditEntry
        {
            Action = AuditAction.KekRotated,
            ResourceType = "Kek",
            ResourceName = _kek.CurrentKekId,
            Details = $"Re-wrapped {secrets} secret(s) and {versions} archived version(s) from KEK {retiredIds} to KEK {_kek.CurrentKekId}",
            IsCritical = true
        });

        // Archived versions are never cached; only a secret's own envelope is.
        if (areSecrets)
        {
            foreach (var row in moved)
            {
                await _cache.EvictAsync(row.Id, CancellationToken.None);
            }
        }

        return moved.Count;
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

    private async Task RecordAndCommitAsync(IDbContextTransaction transaction, AuditEntry entry)
    {
        try
        {
            await _audit.SaveOperationAsync(_db, [entry], CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch (Exception failure)
        {
            throw new AuditWriteFailedException("The rotation batch could not be stored with its audit row, so the pass stopped.", failure);
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
