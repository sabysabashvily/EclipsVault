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
    private readonly IManagedPrincipalLock _logins;

    public SecretService(
        ISecretRepository repository,
        ICryptoEngineFactory cryptoFactory,
        ISecretCache cache,
        IIntrusionResponseService intrusion,
        IAuditSink audit,
        IAuditContext actor,
        IEnumerable<IManagedSecretBackend> managedBackends,
        TimeProvider clock,
        IPremiumFeatureUsage premiumUsage,
        IManagedPrincipalLock logins)
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
        _logins = logins;
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

        return new SecretDetailsDto(
            envelope.Id, envelope.Name, envelope.ProjectKey, envelope.Environment, envelope.Sensitivity,
            envelope.Algorithm, envelope.CreatedAtUtc, envelope.UpdatedAtUtc, envelope.ExpiresAtUtc,
            envelope.IsManaged, envelope.RotationPrincipal);
    }

    public Task RecordViewedAsync(SecretDetailsDto secret, CancellationToken ct)
        => AuditSecretAsync(secret.Id, secret.Name, AuditAction.SecretMetadataViewed, ct);

    public Task RecordAccessDeniedAsync(SecretDetailsDto secret, IEnumerable<string> reasons, CancellationToken ct)
        => _audit.WriteAsync(new AuditEntry
        {
            Action = AuditAction.SecretAccessDenied,
            ResourceType = nameof(Secret),
            ResourceId = secret.Id,
            ResourceName = secret.Name,
            Details = string.Join(" ", reasons),
        }, ct);

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
        // Checked first so an ordinary taken name is refused without a failed save; the unique index
        // still decides a race.
        if (await _repository.ExistsByNameAsync(request.Name.Trim(), ct))
        {
            throw new SecretNameTakenException();
        }

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
        await using var login = await LockLoginAsync(entity, ct);
        await StoreRotatedValueAsync(entity, Guid.NewGuid(), newValue, changeNote, renewTtlDays, AuditAction.SecretUpdated, ct);
        await _cache.EvictAsync(entity.Id, ct);
    }

    /// <summary>
    /// For a managed secret, the lock on its backend login, then the secret read again under it: held from
    /// that read through the commit or put-back, so no other writer of the login can act in between. Null
    /// for a secret no login is bound to.
    /// </summary>
    /// <exception cref="ChangeInProgressException">Another writer holds the login.</exception>
    private async Task<IAsyncDisposable?> LockLoginAsync(Secret entity, CancellationToken ct)
    {
        if (!entity.IsManaged)
        {
            return null;
        }

        var login = await _logins.TryAcquireAsync(entity.RotationBackend!.Value, entity.RotationPrincipal!, ct)
            ?? throw new ChangeInProgressException();
        try
        {
            await _repository.ReloadAsync(entity, ct);
            return entity.IsShredded ? throw new SecretChangedException() : login;
        }
        catch
        {
            await login.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Archives the current value and commits a new one with its audit row. Shared by the two
    /// rotations — the operator pasting a value, and the vault re-passwording a real principal — so
    /// they cannot drift in how a rotation is stored. Clearing the cache is the caller's next step.
    /// </summary>
    private async Task StoreRotatedValueAsync(
        Secret entity, Guid archivedVersionId, string newValue, string? changeNote, int? renewTtlDays, AuditAction recordedAs,
        CancellationToken ct)
    {
        var readWrappedDek = entity.WrappedDek;
        var archived = await ArchiveCurrentAsync(entity, archivedVersionId, changeNote, ct);

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

        await _repository.RotateAsync(entity, readWrappedDek, archived, recordedAs, ct);
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

        await using var login = await LockLoginAsync(entity, ct);
        var principal = entity.RotationPrincipal!;
        var newPassword = CredentialMint.NewPassword();

        // Keep the current value so the upstream change can be undone. Held only for this call.
        var engine = _cryptoFactory.Create();
        var previousPassword = await engine.UnsealAsync(
            entity.ToSealedSecret(), SecretBinding.ForCurrentValue(entity.Id), ct);

        try
        {
            if (!backend.CanApplyPassword(previousPassword))
            {
                throw new UpstreamRotationRefusedException(
                    new InvalidOperationException("The stored password cannot be restored by the managed rotation backend if rotation fails."));
            }

            // Change the real credential first: if the backend refuses, the stored value is still the truth.
            try
            {
                await backend.RotatePrincipalAsync(principal, newPassword, ct);
            }
            catch (UpstreamRotationRefusedException)
            {
                throw;
            }
            catch (Exception uncertain)
            {
                // Any other failure does not prove the statement never ran (a lost reply, a caller who
                // left), and setting the old password again changes nothing if it did not.
                await PutBackUpstreamAsync(backend, principal, previousPassword, entity);
                if (uncertain is OperationCanceledException)
                {
                    throw;
                }

                throw new UpstreamRotationRefusedException(uncertain);
            }

            var archivedVersionId = Guid.NewGuid();
            try
            {
                await StoreRotatedValueAsync(
                    entity, archivedVersionId, newPassword, $"Upstream rotation of '{principal}'", renewTtlDays,
                    AuditAction.SecretUpstreamRotated, ct);
            }
            catch
            {
                // A commit whose reply was lost already holds the new password; only a write that never
                // committed leaves the login on a password the vault does not hold, so only that is undone.
                if (!await StoredAnywayAsync(entity, archivedVersionId, principal))
                {
                    await PutBackUpstreamAsync(backend, principal, previousPassword, entity);
                    throw;
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(previousPassword);
        }

        // Committed: the login and the stored value agree, so nothing from here on undoes the rotation.
        await _cache.EvictAsync(entity.Id, ct);
    }

    /// <summary>
    /// Whether a rotation whose write reported failure committed after all, read afresh and whatever the
    /// caller does meanwhile. When even that cannot be read, the login and the stored value may disagree.
    /// </summary>
    private async Task<bool> StoredAnywayAsync(Secret entity, Guid archivedVersionId, string principal)
    {
        try
        {
            return await _repository.FindVersionAsync(entity.Id, archivedVersionId, CancellationToken.None) is not null;
        }
        catch (Exception readFailure)
        {
            throw new UpstreamRotationDriftedException(principal, await RecordDriftAsync(principal, entity), readFailure);
        }
    }

    /// <summary>
    /// Puts the login back to the password the vault still holds, whether or not the caller is still
    /// waiting: a caller who leaves must not strand the login on a password nobody stored. If that
    /// fails too, only a human can reconcile the two, so the drift is recorded as critical.
    /// </summary>
    private async Task PutBackUpstreamAsync(IManagedSecretBackend backend, string principal, byte[] previousPassword, Secret entity)
    {
        try
        {
            await backend.RotatePrincipalAsync(principal, Encoding.UTF8.GetString(previousPassword), CancellationToken.None);
        }
        catch (Exception putBackFailure)
        {
            throw new UpstreamRotationDriftedException(principal, await RecordDriftAsync(principal, entity), putBackFailure);
        }
    }

    /// <summary>Writes the critical drift row; false when even that cannot be written.</summary>
    private async Task<bool> RecordDriftAsync(string principal, Secret entity)
    {
        try
        {
            await _audit.WriteAsync(new AuditEntry
            {
                Action = AuditAction.SecretUpstreamRotationDrifted,
                ResourceType = nameof(Secret),
                ResourceId = entity.Id,
                ResourceName = entity.Name,
                Details = UpstreamRotationDriftedException.DriftSentence(principal),
                IsCritical = true
            }, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
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
        if (entity.IsManaged)
        {
            throw new RestoreManagedException(entity.RotationPrincipal!);
        }

        var version = await _repository.FindVersionAsync(id, versionId, ct)
                      ?? throw new SecretNotFoundException(versionId);

        var readWrappedDek = entity.WrappedDek;
        var archived = await ArchiveCurrentAsync(entity, Guid.NewGuid(), $"Superseded by restore of version {version.VersionNumber}", ct);

        // Re-seal the chosen version into the live secret's binding. Copying the envelope across is
        // precisely the move the binding exists to stop — an archived value put back as the current
        // one — and refusing to do it by hand is what leaves that signature meaningful when someone
        // does it in the database instead. Here it is a real restore: audited, and access-controlled.
        var restored = await ResealForCurrentValueAsync(entity, version, ct);
        entity.ApplyEnvelope(restored);
        entity.UpdatedAtUtc = _clock.GetUtcNow();

        await _repository.RotateAsync(entity, readWrappedDek, archived, AuditAction.SecretVersionRestored, ct);
        await _cache.EvictAsync(id, ct);
    }

    public async Task<bool> ShredIfExpiredAsync(Guid id, CancellationToken ct)
    {
        var entity = await _repository.FindAsync(id, ct);
        var now = _clock.GetUtcNow();
        if (entity is null || entity.IsShredded || entity.ExpiresAtUtc is not { } expiry || expiry > now)
        {
            return false;
        }

        // A managed secret's other writers hold its login until they commit or put the password back;
        // it is shredded at a later sweep instead.
        await using var login = entity.IsManaged
            ? await _logins.TryAcquireAsync(entity.RotationBackend!.Value, entity.RotationPrincipal!, ct)
            : null;
        if (entity.IsManaged && login is null)
        {
            return false;
        }

        var readWrappedDek = entity.WrappedDek;
        entity.Shred(now);
        if (!await _repository.ShredAsync(entity, readWrappedDek, expiry, ct))
        {
            return false;
        }

        await _cache.EvictAsync(id, ct);
        return true;
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
    private async Task<SecretVersion> ArchiveCurrentAsync(Secret entity, Guid versionId, string? changeNote, CancellationToken ct)
    {
        var resealed = await ResealForVersionAsync(entity, versionId, ct);

        return new SecretVersion
        {
            Id = versionId,
            SecretId = entity.Id,
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
