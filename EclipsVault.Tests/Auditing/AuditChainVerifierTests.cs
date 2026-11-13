using System.Security.Cryptography;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.Auditing;

/// <summary>Verify against each way the trail can stand, from a trail of five entries signed at #3.</summary>
public class AuditChainVerifierTests
{
    private const string Anchored =
        "The signed checkpoint at sequence 3 matches, so entries up to #3 are proven unchanged against the vault's signing key; " +
        "later entries are covered by the hash chain until the next checkpoint. " +
        "To prove the trail to an outside party, export a bundle and verify it offline against the vault's public key.";

    private const string Unanchored =
        "Without a checkpoint, someone with database access could delete the newest entries or rewrite the trail " +
        "and recompute its hashes without detection here. Create a signed checkpoint.";

    private static readonly ECDsa VaultKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private static readonly ECDsa OtherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private static readonly AuditLog[] Trail = Chain(5);

    private static readonly Dictionary<string, (AuditLog[] Entries, AuditCheckpoint[] Checkpoints)> Trails = new()
    {
        ["intact, signed at #3"] = (Trail, [Checkpoint(Trail[2], signedBy: VaultKey)]),
        ["intact, never signed"] = (Trail, []),
        ["#5 deleted, after the checkpoint"] = (Trail[..4], [Checkpoint(Trail[2], signedBy: VaultKey)]),
        ["#2 onwards deleted, back past the checkpoint"] = (Trail[..1], [Checkpoint(Trail[2], signedBy: VaultKey)]),
        ["#2 onwards deleted, valid #3 and invalid #5 checkpoints"] =
            (Trail[..1], [Checkpoint(Trail[2], signedBy: VaultKey), Checkpoint(Trail[4], signedBy: OtherKey, namedAs: VaultKey)]),
        ["#2 onwards deleted, invalid #3 and valid #5 checkpoints"] =
            (Trail[..1], [Checkpoint(Trail[2], signedBy: OtherKey, namedAs: VaultKey), Checkpoint(Trail[4], signedBy: VaultKey)]),
        ["#2 onwards deleted, both checkpoints invalid"] =
            (Trail[..1], [Checkpoint(Trail[2], signedBy: OtherKey, namedAs: VaultKey), Checkpoint(Trail[4], signedBy: OtherKey, namedAs: VaultKey)]),
        ["#2 onwards deleted, both checkpoints valid"] =
            (Trail[..1], [Checkpoint(Trail[2], signedBy: VaultKey), Checkpoint(Trail[4], signedBy: VaultKey)]),
        ["#2 rewritten and every later hash recomputed"] = (Chain(5, rewritten: 2), [Checkpoint(Trail[2], signedBy: VaultKey)]),
        ["checkpoint naming the vault's key, signed by another"] = (Trail, [Checkpoint(Trail[2], signedBy: OtherKey, namedAs: VaultKey)]),
        ["checkpoint signed by another key"] = (Trail, [Checkpoint(Trail[2], signedBy: OtherKey)]),
        ["#2 rewritten, its checkpoint relabelled as another key's"] =
            (Chain(5, rewritten: 2), [Checkpoint(Trail[2], signedBy: VaultKey, namedAs: OtherKey)]),
        ["empty, never signed"] = ([], []),
    };

    public static TheoryData<string, AuditChainOutcome, long, long?, long?, int, int, string> Table => new()
    {
        { "intact, signed at #3", AuditChainOutcome.Anchored, 5, 3, null, 1, 0, $"5 entries verified. {Anchored}" },
        { "intact, never signed", AuditChainOutcome.Unanchored, 5, null, null, 0, 0, $"5 entries verified. {Unanchored}" },
        { "#5 deleted, after the checkpoint", AuditChainOutcome.Anchored, 4, 3, null, 1, 0, $"4 entries verified. {Anchored}" },
        {
            "#2 onwards deleted, back past the checkpoint", AuditChainOutcome.Tampered, 1, null, 2, 0, 0,
            "The chain ends at #1, before the signed checkpoint at #3: entries were deleted. (First broken entry: #2.)"
        },
        {
            "#2 onwards deleted, valid #3 and invalid #5 checkpoints", AuditChainOutcome.Tampered, 1, null, 2, 0, 0,
            "The chain ends at #1, before the signed checkpoint at #3: entries were deleted. (First broken entry: #2.)"
        },
        {
            "#2 onwards deleted, invalid #3 and valid #5 checkpoints", AuditChainOutcome.Tampered, 1, null, 2, 0, 0,
            "The chain ends at #1, before the signed checkpoint at #5: entries were deleted. (First broken entry: #2.)"
        },
        {
            "#2 onwards deleted, both checkpoints invalid", AuditChainOutcome.Tampered, 1, null, 3, 0, 0,
            "The checkpoint at sequence 3 has an invalid signature. (First broken entry: #3.)"
        },
        {
            "#2 onwards deleted, both checkpoints valid", AuditChainOutcome.Tampered, 1, null, 2, 0, 0,
            "The chain ends at #1, before the signed checkpoint at #5: entries were deleted. (First broken entry: #2.)"
        },
        {
            "#2 rewritten and every later hash recomputed", AuditChainOutcome.Tampered, 2, null, 3, 0, 0,
            "Entry #3 does not match the signed checkpoint: the trail was rewritten. (First broken entry: #3.)"
        },
        {
            "checkpoint naming the vault's key, signed by another", AuditChainOutcome.Tampered, 2, null, 3, 0, 0,
            "The checkpoint at sequence 3 has an invalid signature. (First broken entry: #3.)"
        },
        {
            "checkpoint signed by another key", AuditChainOutcome.Unanchored, 5, null, null, 0, 1,
            $"5 entries verified. {Unanchored} 1 checkpoint(s) signed by another key were not checked."
        },
        {
            "#2 rewritten, its checkpoint relabelled as another key's", AuditChainOutcome.Tampered, 2, null, 3, 0, 0,
            "Entry #3 does not match the signed checkpoint: the trail was rewritten. (First broken entry: #3.)"
        },
        { "empty, never signed", AuditChainOutcome.Unanchored, 0, null, null, 0, 0, $"0 entries verified. {Unanchored}" },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public async Task Verify_reports_each_trail_as_the_table_says(
        string trail, AuditChainOutcome outcome, long entriesVerified, long? anchoredAt, long? firstBroken,
        int checkpointsChecked, int checkpointsNotChecked, string message)
    {
        var (entries, checkpoints) = Trails[trail];

        var report = await AuditChainVerifier.VerifyAsync(
            entries.ToAsyncEnumerable(), checkpoints, VaultKey.ExportSubjectPublicKeyInfo(), CancellationToken.None);

        Assert.Equal(
            new AuditIntegrityReport(outcome, entriesVerified, anchoredAt, firstBroken, checkpointsChecked, checkpointsNotChecked, message),
            report);
    }

    /// <summary>A chain whose entries depend only on their sequence, so two chains differ only from the rewritten entry on.</summary>
    private static AuditLog[] Chain(int length, long rewritten = 0)
    {
        var entries = new AuditLog[length];
        var previousHash = AuditRowHasher.GenesisHash;
        for (var sequence = 1; sequence <= length; sequence++)
        {
            var entry = new AuditLog
            {
                Id = new Guid(sequence, 0, 0, new byte[8]),
                Sequence = sequence,
                TimestampUtc = DateTimeOffset.UnixEpoch.AddMinutes(sequence),
                Username = "alice",
                SourceIp = "203.0.113.5",
                Action = AuditAction.SecretRevealed,
                ResourceType = "Secret",
                Details = sequence == rewritten ? "rewritten by someone with database access" : $"entry {sequence}",
                PreviousHash = previousHash,
                HashVersion = AuditRowHasher.CurrentVersion
            };
            entry.EntryHash = AuditRowHasher.Compute(entry, previousHash);
            previousHash = entry.EntryHash;
            entries[sequence - 1] = entry;
        }

        return entries;
    }

    private static AuditCheckpoint Checkpoint(AuditLog head, ECDsa signedBy, ECDsa? namedAs = null)
    {
        var createdAt = DateTimeOffset.UnixEpoch.AddHours(1);
        return new AuditCheckpoint
        {
            Id = Guid.NewGuid(),
            Sequence = head.Sequence,
            ChainHeadHash = head.EntryHash!,
            CreatedAtUtc = createdAt,
            Signature = signedBy.SignData(AuditCheckpointCanonical.Bytes(head.Sequence, head.EntryHash!, createdAt), HashAlgorithmName.SHA256),
            SigningKeyId = AuditSigningKeyId.For((namedAs ?? signedBy).ExportSubjectPublicKeyInfo())
        };
    }
}
