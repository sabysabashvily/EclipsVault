namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Output of an envelope-encryption operation: the payload sealed with a single-use
/// DEK, and that DEK sealed with the master KEK.
/// </summary>
/// <remarks>
/// Equality is made structural on purpose. A record compares its <c>byte[]</c> members by reference,
/// so two envelopes with byte-identical payloads would otherwise be unequal — a trap for a future
/// <c>Distinct</c>, <c>Contains</c>, dictionary key, or <c>Assert.Equal</c>. Comparing the bytes here
/// once makes the surprise impossible rather than latent.
/// </remarks>
public sealed record SealedSecret(byte[] Ciphertext, byte[] WrappedDek, string KekId, string Algorithm)
{
    public bool Equals(SealedSecret? other)
        => other is not null
           && Ciphertext.AsSpan().SequenceEqual(other.Ciphertext)
           && WrappedDek.AsSpan().SequenceEqual(other.WrappedDek)
           && string.Equals(KekId, other.KekId, StringComparison.Ordinal)
           && string.Equals(Algorithm, other.Algorithm, StringComparison.Ordinal);

    // Lengths (not contents) keep this cheap while staying consistent with Equals: equal envelopes
    // share both lengths and both strings, so they always hash the same.
    public override int GetHashCode()
        => HashCode.Combine(Ciphertext.Length, WrappedDek.Length, KekId, Algorithm);
}

/// <summary>
/// The master keys as the active engine sees them: the one it wraps new data keys under, and every
/// one it can still unwrap, current first. <paramref name="KeyService"/> is true when an external key
/// service holds them rather than this server's environment.
/// </summary>
public sealed record MasterKeys(string CurrentKekId, IReadOnlyList<string> KnownKekIds, bool KeyService);

/// <summary>
/// Envelope-encryption engine. The default implementation is local AES-256-GCM;
/// swapping to a cloud KMS is a configuration change (see ICryptoEngineFactory),
/// the business layer never changes.
///
/// The contract is asynchronous because a KMS/HSM-backed engine wraps and unwraps the DEK over the
/// network — the local engine completes synchronously, but the interface must not force a
/// network-backed one into blocking a thread-pool thread on every seal and unseal.
/// </summary>
public interface ICryptoEngine
{
    string EngineId { get; }

    /// <param name="associatedData">
    /// Binds the payload to the row it is being stored in — see <see cref="SecretBinding"/>. It is
    /// authenticated but not encrypted, and the identical value must be supplied to
    /// <see cref="UnsealAsync"/> or the read fails.
    /// </param>
    Task<SealedSecret> SealAsync(byte[] plaintext, byte[] associatedData, CancellationToken ct);

    /// <param name="associatedData">
    /// The binding this payload was sealed with. If it does not match — because the envelope was
    /// moved into a different row — the tag check fails and nothing is returned.
    /// </param>
    Task<byte[]> UnsealAsync(SealedSecret sealedSecret, byte[] associatedData, CancellationToken ct);

    /// <summary>
    /// Re-wraps an already-sealed secret's DEK under the <em>current</em> KEK — used by key
    /// rotation. The payload ciphertext is untouched (no re-encryption); only the wrapped DEK
    /// and its <see cref="SealedSecret.KekId"/> change. The DEK is unwrapped with whichever KEK
    /// originally sealed it, so the engine must still hold that (now retired) key.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="System.Security.Cryptography.CryptographicException"/> when this one data key
    /// cannot be opened, and <see cref="Domain.Exceptions.CryptoConfigurationException"/> when the
    /// engine cannot work at all, such as an unreachable key service.
    /// </remarks>
    Task<SealedSecret> RewrapAsync(SealedSecret sealedSecret, CancellationToken ct);

    /// <summary>The master key ids this engine stores beside each wrapped data key, so rows compare with them.</summary>
    Task<MasterKeys> GetMasterKeysAsync(CancellationToken ct);
}
