using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Auditing;

/// <summary>
/// Signs the audit hash-chain head and builds portable, externally verifiable export bundles.
/// The signer holds the private key; this service just assembles what to sign and what to ship.
/// </summary>
public sealed class AuditCheckpointService : IAuditCheckpointService
{
    private readonly EclipsVaultDbContext _db;
    private readonly IAuditCheckpointSigner _signer;
    private readonly IAuditSink _audit;
    private readonly TimeProvider _clock;
    private readonly IPremiumFeatureUsage _premiumUsage;

    public AuditCheckpointService(EclipsVaultDbContext db, IAuditCheckpointSigner signer, IAuditSink audit, TimeProvider clock, IPremiumFeatureUsage premiumUsage)
    {
        _db = db;
        _signer = signer;
        _audit = audit;
        _clock = clock;
        _premiumUsage = premiumUsage;
    }

    public string SigningKeyId => _signer.KeyId;

    public async Task<AuditCheckpointDto?> CreateCheckpointAsync(CancellationToken ct)
    {
        // Soft licensing signal — never blocks checkpointing.
        await _premiumUsage.RecordUseAsync(LicenseFeatures.AuditAttestation, ct);

        var head = await HeadAsync(ct);
        if (head is null)
        {
            return null; // nothing chained yet
        }

        var checkpoint = SignHead(head.Value.Sequence, head.Value.Hash);
        _db.AuditCheckpoints.Add(checkpoint);
        try
        {
            // The interceptor saves the checkpoint's audit row in the same transaction, so any failed
            // save is the fail-closed audit failure and leaves no checkpoint.
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new AuditWriteFailedException("The checkpoint and its audit row could not be stored.", failure);
        }

        return ToDto(checkpoint);
    }

    public async Task<AuditCheckpointDto?> GetLatestAsync(CancellationToken ct)
    {
        var latest = await _db.AuditCheckpoints.AsNoTracking()
            .OrderByDescending(c => c.Sequence)
            .FirstOrDefaultAsync(ct);
        return latest is null ? null : ToDto(latest);
    }

    public async Task<AuditBundle> ExportAsync(CancellationToken ct)
    {
        // Soft licensing signal — never blocks the export.
        await _premiumUsage.RecordUseAsync(LicenseFeatures.AuditAttestation, ct);

        // Snapshot the chained rows, then sign exactly the head we exported so the bundle is
        // internally consistent (the "export" audit row written below lands after this head).
        var rows = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.Sequence > 0)
            .OrderBy(a => a.Sequence)
            .Select(a => new AuditBundleRow(
                a.Sequence, a.Id, a.TimestampUtc, a.UserId, a.Username, a.SourceIp, (int)a.Action,
                a.ResourceType, a.ResourceId, a.ResourceName, a.Details, a.IsCritical,
                a.PreviousHash!, a.EntryHash!, a.HashVersion))
            .ToListAsync(ct);

        var headSequence = rows.Count == 0 ? 0 : rows[^1].Sequence;
        var headHash = rows.Count == 0 ? AuditRowHasher.GenesisHash : rows[^1].EntryHash;
        var checkpoint = SignHead(headSequence, headHash);

        await _audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.AuditBundleExported,
            ResourceType = nameof(AuditCheckpoint),
            Details = $"Exported a signed audit bundle of {rows.Count} row(s) at sequence {headSequence}"
        }, ct);

        return new AuditBundle(
            AuditBundleSchema.Current,
            _clock.GetUtcNow(),
            _signer.PublicKeySpki,
            new AuditBundleCheckpoint(checkpoint.Sequence, checkpoint.ChainHeadHash, checkpoint.CreatedAtUtc, checkpoint.SigningKeyId, checkpoint.Signature),
            rows);
    }

    private async Task<(long Sequence, string Hash)?> HeadAsync(CancellationToken ct)
    {
        var head = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.Sequence > 0 && a.EntryHash != null)
            .OrderByDescending(a => a.Sequence)
            .Select(a => new { a.Sequence, a.EntryHash })
            .FirstOrDefaultAsync(ct);
        return head is null ? null : (head.Sequence, head.EntryHash!);
    }

    private AuditCheckpoint SignHead(long sequence, string headHash)
    {
        var now = _clock.GetUtcNow();
        var canonical = AuditCheckpointCanonical.Bytes(sequence, headHash, now);
        return new AuditCheckpoint
        {
            Id = Guid.NewGuid(),
            Sequence = sequence,
            ChainHeadHash = headHash,
            CreatedAtUtc = now,
            Signature = _signer.Sign(canonical),
            SigningKeyId = _signer.KeyId
        };
    }

    private static AuditCheckpointDto ToDto(AuditCheckpoint c)
        => new(c.Sequence, c.ChainHeadHash, c.CreatedAtUtc, c.SigningKeyId);
}
