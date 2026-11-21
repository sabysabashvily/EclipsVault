using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Secrets;

/// <summary>
/// Persistence boundary for secrets. Write operations that must be audited atomically
/// with the change (create/update/shred) are audited by the SaveChanges interceptor;
/// read/share auditing is written separately through <see cref="IAuditSink"/>.
///
/// Every write here is all-or-nothing: if one throws, nothing remains staged for a later write to
/// commit, and a write that did not commit leaves the entity it was given as it was. A throw almost
/// always means no commit, but a commit whose reply is lost throws too. A caller that undoes the outside
/// world on failure — putting an upstream password back, say — must first read afresh whether its
/// write is there, as managed rotation does by the id of the version it archived.
/// </summary>
public interface ISecretRepository
{
    Task<Secret?> FindAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Secret>> ListActiveAsync(DateTimeOffset asOfUtc, CancellationToken ct);

    Task<IReadOnlyList<Secret>> ListExpiredAsync(DateTimeOffset asOfUtc, CancellationToken ct);

    /// <summary>
    /// Live secrets whose deadline falls between now and <paramref name="horizonUtc"/> and that have
    /// not yet been warned about that deadline — the candidates for an expiry notice.
    /// </summary>
    Task<IReadOnlyList<Secret>> ListExpiringAsync(DateTimeOffset asOfUtc, DateTimeOffset horizonUtc, CancellationToken ct);

    /// <summary>
    /// Records that an expiry notice has been sent for the secret's current deadline, writing that
    /// column and nothing else. Distinct from <see cref="UpdateAsync"/> on purpose: this is
    /// bookkeeping, and a whole-entity update would be indistinguishable from a real edit — and so
    /// audited as one, telling an auditor a secret was modified that nobody touched.
    /// </summary>
    Task MarkExpiryNoticeSentAsync(Secret secret, CancellationToken ct);

    /// <summary>Whether any secret, decoy or shredded one included, already has this name.</summary>
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct);

    /// <exception cref="SecretNameTakenException">Another secret took the name first; nothing was saved.</exception>
    /// <exception cref="AuditWriteFailedException">The secret and its audit row could not be stored; nothing was saved.</exception>
    Task AddAsync(Secret secret, CancellationToken ct);

    Task UpdateAsync(Secret secret, CancellationToken ct);

    Task DeleteAsync(Secret secret, CancellationToken ct);

    /// <summary>
    /// Persists a new value, but only while the stored secret is unshredded and its wrapped data key is still
    /// <paramref name="readWrappedDek"/>: inserts the archived version, numbered inside this write, and
    /// updates the secret in one transaction, with one audit row recording the change as
    /// <paramref name="recordedAs"/>.
    /// </summary>
    /// <exception cref="SecretChangedException">Another change committed after the value was read; nothing was saved.</exception>
    /// <exception cref="AuditWriteFailedException">
    /// The write reported failure. It normally saved nothing, but a commit whose reply was lost throws too.
    /// </exception>
    Task RotateAsync(Secret secret, byte[] readWrappedDek, SecretVersion archivedVersion, AuditAction recordedAs, CancellationToken ct);

    /// <summary>
    /// Shreds the secret and purges its archived versions (which hold key material) in one transaction,
    /// but only while its wrapped data key and expiry are still what was read. False when it changed, so
    /// nothing was shredded.
    /// </summary>
    Task<bool> ShredAsync(Secret secret, byte[] readWrappedDek, DateTimeOffset readExpiresAtUtc, CancellationToken ct);

    /// <summary>Replaces what this request holds of the secret with what is stored now.</summary>
    Task ReloadAsync(Secret secret, CancellationToken ct);

    Task<IReadOnlyList<SecretVersion>> ListVersionsAsync(Guid secretId, CancellationToken ct);

    Task<SecretVersion?> FindVersionAsync(Guid secretId, Guid versionId, CancellationToken ct);
}
