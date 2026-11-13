using System.Security.Cryptography;
using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Auditing;

/// <summary>
/// What verifying the audit trail found. <see cref="Message"/> is the text that follows the outcome's
/// title; a tampered trail's ends with its first broken entry. <see cref="CheckpointsChecked"/> counts
/// the checkpoints found to match before any problem.
/// </summary>
public sealed record AuditIntegrityReport(
    AuditChainOutcome Outcome,
    long EntriesVerified,
    long? AnchoredAtSequence,
    long? FirstBrokenSequence,
    int CheckpointsChecked,
    int CheckpointsNotChecked,
    string Message);

/// <summary>
/// Verifies the audit trail against the checkpoints the vault signed with its current key (ADR-0030).
/// It walks the trail once, in sequence order, holding one entry at a time. Every entry must continue
/// the chain from #1; every checkpoint needs a valid signature and the hash of the entry at its
/// sequence; and the trail must reach the latest checkpoint. Checkpoints signed by another key are
/// counted, not checked, because the vault holds only its current public key. A checkpoint counts as
/// the current key's if it is labelled so or its signature verifies under that key. The first problem
/// in sequence order is the one reported.
/// </summary>
public static class AuditChainVerifier
{
    public static async Task<AuditIntegrityReport> VerifyAsync(
        IAsyncEnumerable<AuditLog> trail,
        IReadOnlyCollection<AuditCheckpoint> checkpoints,
        byte[] signingKeySpki,
        CancellationToken ct)
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(signingKeySpki, out _);
        var keyId = AuditSigningKeyId.For(signingKeySpki);
        // The stored key id is not signed, so a checkpoint the current key did sign is checked whatever it is labelled.
        var signed = checkpoints
            .Where(c => c.SigningKeyId == keyId || IsValidlySigned(c, key))
            .OrderBy(c => c.Sequence)
            .ToList();
        var notChecked = checkpoints.Count - signed.Count;

        long verified = 0;
        var matched = 0;
        var previousHash = AuditRowHasher.GenesisHash;
        await foreach (var entry in trail.WithCancellation(ct))
        {
            if (ChainBreak(entry, verified + 1, previousHash) is { } reason)
            {
                return Tampered(reason, entry.Sequence);
            }

            for (; matched < signed.Count && signed[matched].Sequence <= entry.Sequence; matched++)
            {
                var checkpoint = signed[matched];
                if (!IsValidlySigned(checkpoint, key))
                {
                    return Tampered($"The checkpoint at sequence {checkpoint.Sequence} has an invalid signature.", checkpoint.Sequence);
                }

                if (checkpoint.Sequence != entry.Sequence || checkpoint.ChainHeadHash != entry.EntryHash)
                {
                    return Tampered($"Entry #{checkpoint.Sequence} does not match the signed checkpoint: the trail was rewritten.", checkpoint.Sequence);
                }
            }

            previousHash = entry.EntryHash!;
            verified++;
        }

        // Checkpoints past the end prove entries existed that are gone, unless they are forged.
        if (signed.Skip(matched).FirstOrDefault(c => !IsValidlySigned(c, key)) is { } forged)
        {
            return Tampered($"The checkpoint at sequence {forged.Sequence} has an invalid signature.", forged.Sequence);
        }

        if (matched < signed.Count)
        {
            return Tampered($"The chain ends at #{verified}, before the signed checkpoint at #{signed[^1].Sequence}: entries were deleted.", verified + 1);
        }

        if (signed.Count == 0)
        {
            return Report(AuditChainOutcome.Unanchored, null, null,
                $"{verified} entries verified. Without a checkpoint, someone with database access could delete the newest entries " +
                "or rewrite the trail and recompute its hashes without detection here. Create a signed checkpoint.");
        }

        var anchor = signed[^1].Sequence;
        return Report(AuditChainOutcome.Anchored, anchor, null,
            $"{verified} entries verified. The signed checkpoint at sequence {anchor} matches, so entries up to #{anchor} are proven " +
            "unchanged against the vault's signing key; later entries are covered by the hash chain until the next checkpoint. " +
            "To prove the trail to an outside party, export a bundle and verify it offline against the vault's public key.");

        AuditIntegrityReport Tampered(string reason, long firstBroken)
            => Report(AuditChainOutcome.Tampered, null, firstBroken, $"{reason} (First broken entry: #{firstBroken}.)");

        AuditIntegrityReport Report(AuditChainOutcome outcome, long? anchoredAt, long? firstBroken, string message)
            => new(outcome, verified, anchoredAt, firstBroken, matched, notChecked, notChecked == 0
                ? message
                : $"{message} {notChecked} checkpoint(s) signed by another key were not checked.");
    }

    private static string? ChainBreak(AuditLog entry, long expectedSequence, string previousHash)
    {
        if (entry.Sequence != expectedSequence)
        {
            return $"Sequence gap before entry #{entry.Sequence} — a row was deleted or inserted.";
        }

        if (entry.PreviousHash != previousHash)
        {
            return $"Entry #{entry.Sequence} does not link to the previous entry — the chain was broken here.";
        }

        return AuditRowHasher.Compute(entry, previousHash) == entry.EntryHash
            ? null
            : $"Entry #{entry.Sequence} has been altered — its hash no longer matches its content.";
    }

    private static bool IsValidlySigned(AuditCheckpoint checkpoint, ECDsa key)
    {
        try
        {
            var canonical = AuditCheckpointCanonical.Bytes(checkpoint.Sequence, checkpoint.ChainHeadHash, checkpoint.CreatedAtUtc);
            return key.VerifyData(canonical, checkpoint.Signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            // A stored signature that is not even well-formed is as forged as a wrong one.
            return false;
        }
    }
}
