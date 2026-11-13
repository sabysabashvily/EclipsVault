using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Locking;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Auditing;

/// <summary>
/// Signs the audit hash-chain head, verifies the trail against its checkpoints and builds portable,
/// externally verifiable export bundles, one verification and one export at a time across replicas.
/// The signer holds the private key; this service just assembles what to sign and what to ship.
/// </summary>
public sealed class AuditCheckpointService : IAuditCheckpointService
{
    private const string VerifyLockResource = "EclipsVault:AuditVerify";

    private const string ExportLockResource = "EclipsVault:AuditExport";

    private const string AuditTrail = "AuditTrail";

    private readonly EclipsVaultDbContext _db;
    private readonly IAuditCheckpointSigner _signer;
    private readonly IAuditSink _audit;
    private readonly TimeProvider _clock;
    private readonly IPremiumFeatureUsage _premiumUsage;
    private readonly ISessionLocker _locks;

    public AuditCheckpointService(
        EclipsVaultDbContext db, IAuditCheckpointSigner signer, IAuditSink audit, TimeProvider clock, IPremiumFeatureUsage premiumUsage, ISessionLocker locks)
    {
        _db = db;
        _signer = signer;
        _audit = audit;
        _clock = clock;
        _premiumUsage = premiumUsage;
        _locks = locks;
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

    public async Task<AuditIntegrityReport> VerifyAsync(CancellationToken ct)
    {
        // Owned by the database session, so it holds until the verification is recorded, on every
        // replica, and the database releases it if this process dies mid-walk.
        await using var turn = await _locks.TryAcquireAsync(_db, VerifyLockResource, ct)
            ?? throw new AuditBusyException();

        var checkpoints = await _db.AuditCheckpoints.AsNoTracking().ToListAsync(ct);
        var trail = _db.AuditLogs.AsNoTracking()
            .Where(a => a.Sequence > 0)
            .OrderBy(a => a.Sequence)
            .AsAsyncEnumerable();
        var report = await AuditChainVerifier.VerifyAsync(trail, checkpoints, _signer.PublicKeySpki, ct);

        var tampered = report.Outcome == AuditChainOutcome.Tampered;
        await _audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.AuditChainVerified,
            ResourceType = AuditTrail,
            Details = tampered ? $"Tampering detected. {report.Message}" : report.Message,
            IsCritical = tampered
        }, ct);

        return report;
    }

    public async Task<AuditCheckpointDto?> GetLatestAsync(CancellationToken ct)
    {
        var latest = await _db.AuditCheckpoints.AsNoTracking()
            .OrderByDescending(c => c.Sequence)
            .FirstOrDefaultAsync(ct);
        return latest is null ? null : ToDto(latest);
    }

    public async Task<AuditBundleExport> ExportAsync(CancellationToken ct)
    {
        // Soft licensing signal — never blocks the export.
        await _premiumUsage.RecordUseAsync(LicenseFeatures.AuditAttestation, ct);

        // Owned by the database session, so it holds while the rows stream out, on every replica.
        var turn = await _locks.TryAcquireAsync(_db, ExportLockResource, ct) ?? throw new AuditBusyException();
        try
        {
            // The export's own row lands after the signed head, so the bundle stays internally consistent.
            var (headSequence, headHash) = await HeadAsync(ct) ?? (0, AuditRowHasher.GenesisHash);
            var checkpoint = SignHead(headSequence, headHash);
            var chained = _db.AuditLogs.AsNoTracking().Where(a => a.Sequence > 0 && a.Sequence <= headSequence);

            await _audit.WriteAsync(new AuditEntry
            {
                Action = AuditAction.AuditBundleExported,
                ResourceType = nameof(AuditCheckpoint),
                Details = $"Exported a signed audit bundle of {await chained.CountAsync(ct)} row(s) at sequence {headSequence}"
            }, ct);

            var rows = chained
                .OrderBy(a => a.Sequence)
                .Select(a => new AuditBundleRow(
                    a.Sequence, a.Id, a.TimestampUtc, a.UserId, a.Username, a.SourceIp, (int)a.Action,
                    a.ResourceType, a.ResourceId, a.ResourceName, a.Details, a.IsCritical,
                    a.PreviousHash!, a.EntryHash!, a.HashVersion))
                .AsAsyncEnumerable();

            return new AuditBundleExport(
                _clock.GetAuditUtcNow(),
                _signer.PublicKeySpki,
                new AuditBundleCheckpoint(checkpoint.Sequence, checkpoint.ChainHeadHash, checkpoint.CreatedAtUtc, checkpoint.SigningKeyId, checkpoint.Signature),
                rows,
                turn);
        }
        catch
        {
            await turn.DisposeAsync();
            throw;
        }
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
        var now = _clock.GetAuditUtcNow();
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
