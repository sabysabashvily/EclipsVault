using System.Security.Cryptography;
using System.Text;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Secrets;

/// <summary>
/// Secret lifecycle orchestration: cache-aside reads of encrypted envelopes,
/// honey-token trap wire, fail-closed audit ordering, envelope encryption via the
/// engine factory, and eager cache eviction on every mutation.
/// </summary>
public sealed class SecretService : ISecretService
{
    private readonly ISecretRepository _repository;
    private readonly ICryptoEngineFactory _cryptoFactory;
    private readonly ISecretCache _cache;
    private readonly IIntrusionResponseService _intrusion;
    private readonly IAuditSink _audit;
    private readonly IAuditContext _actor;
    private readonly IReadOnlyCollection<IManagedSecretBackend> _managedBackends;
    private readonly TimeProvider _clock;
    private readonly IPremiumFeatureUsage _premiumUsage;

    public SecretService(
        ISecretRepository repository,
        ICryptoEngineFactory cryptoFactory,
        ISecretCache cache,
        IIntrusionResponseService intrusion,
        IAuditSink audit,
        IAuditContext actor,
        IEnumerable<IManagedSecretBackend> managedBackends,
        TimeProvider clock,
        IPremiumFeatureUsage premiumUsage)
    {
        _repository = repository;
        _cryptoFactory = cryptoFactory;
        _cache = cache;
        _intrusion = intrusion;
        _audit = audit;
        _actor = actor;
        _managedBackends = [.. managedBackends];
        _clock = clock;
        _premiumUsage = premiumUsage;
    }

    private Task AuditSecretAsync(Guid id, string name, AuditAction action, CancellationToken ct)
        => _audit.WriteAsync(new AuditEntry { Action = action, ResourceType = nameof(Secret), ResourceId = id, ResourceName = name }, ct);

    /// <summary>
    /// Every active secret's metadata, decoys excluded — this is attribute data for the caller's
    /// ABAC filter to narrow, not a list anyone is entitled to see whole.
    ///
    /// Decoys are dropped here, for every caller, rather than being handed out with a flag for the
    /// UI to hide. A decoy that is reachable by following a link is a trap for your own staff:
    /// opening one revokes their session and blocks their network range, and they had no way to
    /// know. Unlisted, the only way to reach one is with an id obtained out of band — from a
    /// database dump, a backup, a stolen envelope — which is exactly the reader a decoy exists to
    /// catch, and makes any read of one unambiguous.
    /// </summary>
    public async Task<IReadOnlyList<SecretSummaryDto>> ListAsync(CancellationToken ct)
    {
        var secrets = await _repository.ListActiveAsync(_clock.GetUtcNow(), ct);
        return secrets
            .Where(s => !s.IsHoneyToken)
            .Select(s => new SecretSummaryDto(s.Id, s.Name, s.ProjectKey, s.Environment, s.Sensitivity, s.CreatedAtUtc, s.ExpiresAtUtc))
            .ToList();
    }

    public async Task<SecretDetailsDto> GetDetailsAsync(Guid id, CancellationToken ct)
    {
        var envelope = await GetEnvelopeAsync(id, ct);
        await TripHoneyTokenIfNeededAsync(envelope, ct);
        await AuditSecretAsync(envelope.Id, envelope.Name, AuditAction.SecretMetadataViewed, ct);

        return new SecretDetailsDto(
            envelope.Id, envelope.Name, envelope.ProjectKey, envelope.Environment, envelope.Sensitivity,
            envelope.Algorithm, envelope.CreatedAtUtc, envelope.UpdatedAtUtc, envelope.ExpiresAtUtc,
            envelope.IsManaged, envelope.RotationPrincipal);
    }

    public async Task<RevealedSecretDto> RevealAsync(Guid id, CancellationToken ct)
    {
        var envelope = await GetEnvelopeAsync(id, ct);
        await TripHoneyTokenIfNeededAsync(envelope, ct);

        // Fail-closed ordering: the audit row is committed BEFORE any decryption.
        // If it cannot be written, no plaintext ever exists for this request.
        await AuditSecretAsync(envelope.Id, envelope.Name, AuditAction.SecretRevealed, ct);

        try
        {
            return await DecryptToDtoAsync(
                envelope, SecretBinding.ForCurrentValue(envelope.Id), envelope.Id, envelope.Name, ct);
        }
        catch (CryptoConfigurationException)
        {
            // A cached envelope can outlive its row's move to the current master key and still name
            // a key the operator has since unloaded (ADR-0029). The row itself has moved, so read it
            // again from the database: the cache may still hold, or be refilled with, the stale entry.
            var row = await LoadEnvelopeAsync(id, ct);
            if (row.KekId == envelope.KekId)
            {
                throw;
            }

            return await DecryptToDtoAsync(row, SecretBinding.ForCurrentValue(row.Id), row.Id, row.Name, ct);
        }
    }

    public async Task<Guid> CreateAsync(CreateSecretRequest request, CancellationToken ct)
    {
        // The id is settled before the value is sealed, because it is what the payload is bound to.
        var id = Guid.NewGuid();
        var sealedSecret = await SealValueAsync(request.Value, SecretBinding.ForCurrentValue(id), ct);
        var now = _clock.GetUtcNow();

        var secret = new Secret
        {
            Id = id,
            Name = request.Name.Trim(),
            ProjectKey = request.ProjectKey.Trim().ToUpperInvariant(),
            Environment = request.Environment,
            Sensitivity = request.Sensitivity,
            Ciphertext = sealedSecret.Ciphertext,
            WrappedDek = sealedSecret.WrappedDek,
            KekId = sealedSecret.KekId,
            Algorithm = sealedSecret.Algorithm,
            CreatedAtUtc = now,
            ExpiresAtUtc = request.TtlDays > 0 ? now.AddDays(request.TtlDays) : null,
            CreatedByUserId = _actor.UserId ?? Guid.Empty
        };

        await _repository.AddAsync(secret, ct);
        await _cache.EvictAsync(secret.Id, ct);
        return secret.Id;
    }

    public async Task RotateAsync(Guid id, string newValue, string? changeNote, int? renewTtlDays, CancellationToken ct)
    {
        var envelope = await GetEnvelopeAsync(id, ct);
        await TripHoneyTokenIfNeededAsync(envelope, ct);

        var entity = await _repository.FindAsync(id, ct) ?? throw new SecretNotFoundException(id);
        await StoreRotatedValueAsync(entity, newValue, changeNote, renewTtlDays, ct);
    }

    /// <summary>
    /// Archives the current value and stores a new one. Shared by the two rotations — the operator
    /// pasting a value, and the vault re-passwording a real principal — so they cannot drift in how
    /// a rotation is recorded.
    /// </summary>
    private async Task StoreRotatedValueAsync(
        Secret entity, string newValue, string? changeNote, int? renewTtlDays, CancellationToken ct)
    {
        var archived = await ArchiveCurrentAsync(entity, changeNote, ct);

        var now = _clock.GetUtcNow();
        var sealedSecret = await SealValueAsync(newValue, SecretBinding.ForCurrentValue(entity.Id), ct);
        entity.ApplyEnvelope(sealedSecret);
        entity.UpdatedAtUtc = now;

        // Renewing is what saves a near-TTL secret from the lifecycle reaper. Without this the new
        // value inherits the old deadline and is shredded on schedule — silently destroying the
        // credential the operator just rotated in. Mirrors CreateAsync: days from now, or leave
        // the existing deadline (including "never") alone.
        if (renewTtlDays is > 0)
        {
            entity.ExpiresAtUtc = now.AddDays(renewTtlDays.Value);
        }

        // One transaction: the interceptor writes a SecretUpdated audit row atomically.
        await _repository.RotateAsync(entity, archived, ct);
        await _cache.EvictAsync(entity.Id, ct);
    }

    public async Task RotateManagedAsync(Guid id, int? renewTtlDays, CancellationToken ct)
    {
        var envelope = await GetEnvelopeAsync(id, ct);
        await TripHoneyTokenIfNeededAsync(envelope, ct);

        var entity = await _repository.FindAsync(id, ct) ?? throw new SecretNotFoundException(id);
        if (!entity.IsManaged)
        {
            throw new VaultAdminException(
                $"'{entity.Name}' is not bound to a backend principal, so the vault cannot change the real credential. " +
                "Rotate it with a new value instead.");
        }

        // Gate: a trial or paid licence is required to have the vault change the real credential.
        // Refused before the backend is touched, so the stored value stays the truth. (The secret and
        // its read/decrypt path are never affected — only re-rotation is gated.)
        await _premiumUsage.RequireAsync(LicenseFeatures.ManagedRotation, ct);

        var backend = _managedBackends.FirstOrDefault(b => b.Backend == entity.RotationBackend)
            ?? throw new VaultAdminException($"No backend is configured for '{entity.RotationBackend}'.");

        var principal = entity.RotationPrincipal!;
        var newPassword = CredentialMint.NewPassword();

        // Keep the current value so the upstream change can be undone. Held only for this call.
        var engine = _cryptoFactory.Create();
        var previousPassword = await engine.UnsealAsync(
            entity.ToSealedSecret(), SecretBinding.ForCurrentValue(entity.Id), ct);

        try
        {
            // Change the real credential first: if the backend refuses, nothing has moved and the
            // stored value is still the truth.
            await backend.RotatePrincipalAsync(principal, newPassword, ct);

            try
            {
                await StoreRotatedValueAsync(entity, newPassword, $"Upstream rotation of '{principal}'", renewTtlDays, ct);
            }
            catch
            {
                // The live credential moved but we could not record it, so the vault is now serving
                // a password that does not work — the exact drift this feature exists to prevent.
                // Put the principal back to the value the vault still holds.
                await UndoUpstreamRotationAsync(backend, principal, previousPassword, entity, ct);
                throw;
            }

            await AuditSecretAsync(entity.Id, entity.Name, AuditAction.SecretUpstreamRotated, ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(previousPassword);
        }
    }

    /// <summary>
    /// Best-effort restore of the upstream password after a failed rotation. If it does not work the
    /// stored value and the live credential are out of step and only a human can reconcile them, so
    /// that is recorded as critical rather than swallowed.
    /// </summary>
    private async Task UndoUpstreamRotationAsync(
        IManagedSecretBackend backend, string principal, byte[] previousPassword, Secret entity, CancellationToken ct)
    {
        try
        {
            await backend.RotatePrincipalAsync(principal, Encoding.UTF8.GetString(previousPassword), ct);
        }
        catch
        {
            await _audit.WriteAsync(new AuditEntry
            {
                Action = AuditAction.SecretUpstreamRotationDrifted,
                ResourceType = nameof(Secret),
                ResourceId = entity.Id,
                ResourceName = entity.Name,
                Details = $"The password of '{principal}' was changed upstream but could not be stored or put back. " +
                          "The stored value no longer opens it — reconcile by hand.",
                IsCritical = true
            }, ct);
        }
    }

    public async Task<IReadOnlyList<SecretVersionDto>> ListVersionsAsync(Guid id, CancellationToken ct)
    {
        var versions = await _repository.ListVersionsAsync(id, ct);
        return versions
            .Select(v => new SecretVersionDto(v.Id, v.VersionNumber, v.ArchivedAtUtc, v.ArchivedBy, v.ChangeNote))
            .ToList();
    }

    public async Task<RevealedSecretDto> RevealVersionAsync(Guid id, Guid versionId, CancellationToken ct)
    {
        var envelope = await GetEnvelopeAsync(id, ct);
        await TripHoneyTokenIfNeededAsync(envelope, ct);

        var version = await _repository.FindVersionAsync(id, versionId, ct)
                      ?? throw new SecretNotFoundException(versionId);

        // Fail-closed: audit committed before any decryption.
        await AuditSecretAsync(id, envelope.Name, AuditAction.SecretVersionRevealed, ct);

        return await DecryptToDtoAsync(
            version, SecretBinding.ForArchivedVersion(id, version.Id), id, envelope.Name, ct);
    }

    /// <summary>
    /// The tail every reveal shares: unseal the envelope under its row binding, hand back the
    /// plaintext as a DTO, and wipe the plaintext buffer no matter how the caller returns. The
    /// audit row is committed by the caller <em>before</em> this runs, so no plaintext exists
    /// unless the reveal was already recorded.
    /// </summary>
    private async Task<RevealedSecretDto> DecryptToDtoAsync(
        IEnvelope carrier, byte[] binding, Guid id, string name, CancellationToken ct)
    {
        var engine = _cryptoFactory.Create();
        var plaintext = await engine.UnsealAsync(carrier.ToSealedSecret(), binding, ct);
        try
        {
            return new RevealedSecretDto(id, name, Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task RestoreVersionAsync(Guid id, Guid versionId, CancellationToken ct)
    {
        var envelope = await GetEnvelopeAsync(id, ct);
        await TripHoneyTokenIfNeededAsync(envelope, ct);

        var entity = await _repository.FindAsync(id, ct) ?? throw new SecretNotFoundException(id);
        var version = await _repository.FindVersionAsync(id, versionId, ct)
                      ?? throw new SecretNotFoundException(versionId);

        var archived = await ArchiveCurrentAsync(entity, $"Superseded by restore of version {version.VersionNumber}", ct);

        // Re-seal the chosen version into the live secret's binding. Copying the envelope across is
        // precisely the move the binding exists to stop — an archived value put back as the current
        // one — and refusing to do it by hand is what leaves that signature meaningful when someone
        // does it in the database instead. Here it is a real restore: audited, and access-controlled.
        var restored = await ResealForCurrentValueAsync(entity, version, ct);
        entity.ApplyEnvelope(restored);
        entity.UpdatedAtUtc = _clock.GetUtcNow();

        await _repository.RotateAsync(entity, archived, ct);
        await AuditSecretAsync(id, envelope.Name, AuditAction.SecretVersionRestored, ct);
        await _cache.EvictAsync(id, ct);
    }

    /// <summary>
    /// Builds a version snapshot of the secret's CURRENT (about-to-be-replaced) value.
    ///
    /// The value is re-sealed onto the new version rather than copied across, because each payload
    /// is bound to the exact row it lives in: the live value's envelope is not decryptable as a
    /// version, by us or by anyone who moves it there by hand. That is the property being paid for,
    /// and it costs one decrypt-and-re-encrypt on an operation that only happens when a secret is
    /// rotated.
    /// </summary>
    private async Task<SecretVersion> ArchiveCurrentAsync(Secret entity, string? changeNote, CancellationToken ct)
    {
        var versionId = Guid.NewGuid();
        var resealed = await ResealForVersionAsync(entity, versionId, ct);

        return new SecretVersion
        {
            Id = versionId,
            SecretId = entity.Id,
            VersionNumber = await _repository.CountVersionsAsync(entity.Id, ct) + 1,
            Ciphertext = resealed.Ciphertext,
            WrappedDek = resealed.WrappedDek,
            KekId = resealed.KekId,
            Algorithm = resealed.Algorithm,
            ArchivedAtUtc = _clock.GetUtcNow(),
            ArchivedBy = _actor.Username ?? "system",
            ChangeNote = changeNote
        };
    }

    /// <summary>Moves the secret's live value into the binding of an archived version.</summary>
    private Task<SealedSecret> ResealForVersionAsync(Secret entity, Guid versionId, CancellationToken ct)
        => ResealAsync(
            entity.ToSealedSecret(),
            SecretBinding.ForCurrentValue(entity.Id),
            SecretBinding.ForArchivedVersion(entity.Id, versionId), ct);

    /// <summary>Moves an archived version's value back into the secret's live binding.</summary>
    private Task<SealedSecret> ResealForCurrentValueAsync(Secret entity, SecretVersion version, CancellationToken ct)
        => ResealAsync(
            version.ToSealedSecret(),
            SecretBinding.ForArchivedVersion(entity.Id, version.Id),
            SecretBinding.ForCurrentValue(entity.Id), ct);

    private async Task<SealedSecret> ResealAsync(SealedSecret sealedSecret, byte[] from, byte[] to, CancellationToken ct)
    {
        var engine = _cryptoFactory.Create();
        var plaintext = await engine.UnsealAsync(sealedSecret, from, ct);
        try
        {
            return await engine.SealAsync(plaintext, to, ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var envelope = await GetEnvelopeAsync(id, ct);
        await TripHoneyTokenIfNeededAsync(envelope, ct);

        var entity = await _repository.FindAsync(id, ct) ?? throw new SecretNotFoundException(id);
        await _repository.DeleteAsync(entity, ct);
        await _cache.EvictAsync(id, ct);
    }

    private async Task<SealedSecret> SealValueAsync(string value, byte[] associatedData, CancellationToken ct)
    {
        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            return await _cryptoFactory.Create().SealAsync(plaintext, associatedData, ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private async Task<EncryptedSecretEnvelope> GetEnvelopeAsync(Guid id, CancellationToken ct)
    {
        var cached = await _cache.GetAsync(id, ct);
        if (cached is not null)
        {
            return cached;
        }

        var envelope = await LoadEnvelopeAsync(id, ct);
        await _cache.SetAsync(envelope, ct);
        return envelope;
    }

    private async Task<EncryptedSecretEnvelope> LoadEnvelopeAsync(Guid id, CancellationToken ct)
    {
        var entity = await _repository.FindAsync(id, ct);
        if (entity is null || entity.IsShredded ||
            (entity.ExpiresAtUtc is { } expiry && expiry <= _clock.GetUtcNow()))
        {
            throw new SecretNotFoundException(id);
        }

        return new EncryptedSecretEnvelope(
            entity.Id, entity.Name, entity.ProjectKey, entity.Environment, entity.Sensitivity,
            entity.Ciphertext, entity.WrappedDek, entity.KekId, entity.Algorithm,
            entity.IsHoneyToken, entity.CreatedAtUtc, entity.UpdatedAtUtc, entity.ExpiresAtUtc,
            entity.IsManaged, entity.RotationPrincipal);
    }

    private async Task TripHoneyTokenIfNeededAsync(EncryptedSecretEnvelope envelope, CancellationToken ct)
    {
        if (!envelope.IsHoneyToken)
        {
            return;
        }

        // Deliberately bypasses ABAC: the trap fires no matter who asks or from where.
        await _intrusion.TriggerHoneyTokenAsync(envelope.Id, envelope.Name, ct);
        throw new HoneyTokenTrippedException(envelope.Id, envelope.Name);
    }
}
