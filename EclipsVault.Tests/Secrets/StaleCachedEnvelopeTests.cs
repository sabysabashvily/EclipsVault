using System.Text;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Tests.Fakes;
using EclipsVault.Tests.TestDoubles;
using Xunit;

namespace EclipsVault.Tests.Secrets;

/// <summary>
/// Master-key rotation clears a secret from the cache after its batch commits, but a read that
/// started before the commit can put the old envelope back, and a failed clear is never retried
/// (ADR-0029). Such an envelope still names the retired master key. Once the operator unloads that
/// key, a reveal served from it must fall back to the row, which already sits under the current key.
/// </summary>
public class StaleCachedEnvelopeTests
{
    private const string RetiredKek = "retired-kek";

    [Fact]
    public async Task A_reveal_from_a_cached_envelope_under_an_unloaded_key_reads_the_row_again()
    {
        var (service, id) = await BuildAsync(rowIsRotated: true);

        var revealed = await service.RevealAsync(id, CancellationToken.None);

        Assert.Equal("s3cret", revealed.Value);
    }

    [Fact]
    public async Task A_row_still_under_the_unloaded_key_still_fails_the_reveal()
    {
        var (service, id) = await BuildAsync(rowIsRotated: false);

        await Assert.ThrowsAsync<CryptoConfigurationException>(() => service.RevealAsync(id, CancellationToken.None));
        // One attempt, not two: under a KMS engine each failed unseal can take a full network timeout.
        Assert.Equal(1, _engine!.UnsealAttempts);
    }

    [Fact]
    public async Task A_row_shredded_since_it_was_cached_is_not_found()
    {
        var (service, id) = await BuildAsync(rowIsRotated: true, rowIsShredded: true);

        await Assert.ThrowsAsync<SecretNotFoundException>(() => service.RevealAsync(id, CancellationToken.None));
    }

    /// <summary>A cached envelope under the unloaded retired key, over a row that has or has not moved.</summary>
    private RetiredKeyUnloaded? _engine;

    private async Task<(SecretService Service, Guid Id)> BuildAsync(bool rowIsRotated, bool rowIsShredded = false)
    {
        var engine = new FakeCryptoEngine();
        var id = Guid.NewGuid();
        var sealedValue = await engine.SealAsync(Encoding.UTF8.GetBytes("s3cret"), SecretBinding.ForCurrentValue(id), CancellationToken.None);
        var row = new Secret
        {
            Id = id,
            Name = "Billing_Db",
            ProjectKey = "GLOBAL",
            Environment = SecretEnvironment.Production,
            Sensitivity = SensitivityLevel.Internal,
            Ciphertext = sealedValue.Ciphertext,
            WrappedDek = sealedValue.WrappedDek,
            KekId = rowIsRotated ? sealedValue.KekId : RetiredKek,
            Algorithm = sealedValue.Algorithm,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            IsShredded = rowIsShredded
        };
        var cache = new StaleCache(new EncryptedSecretEnvelope(
            row.Id, row.Name, row.ProjectKey, row.Environment, row.Sensitivity, row.Ciphertext, [], RetiredKek,
            row.Algorithm, row.IsHoneyToken, row.CreatedAtUtc, null, null));
        var service = new SecretService(
            new OneRowRepository(row), _engine = new RetiredKeyUnloaded(engine), cache, new NoIntrusion(), new NullAuditSink(),
            new StubActor(), [], TimeProvider.System, new RecordingPremiumFeatureUsage());
        return (service, id);
    }

    /// <summary>The engine after the operator has unloaded the retired key.</summary>
    private sealed class RetiredKeyUnloaded(FakeCryptoEngine inner) : ICryptoEngine, ICryptoEngineFactory
    {
        public string EngineId => inner.EngineId;

        public ICryptoEngine Create() => this;

        public Task<SealedSecret> SealAsync(byte[] plaintext, byte[] associatedData, CancellationToken ct)
            => inner.SealAsync(plaintext, associatedData, ct);

        public int UnsealAttempts { get; private set; }

        public Task<byte[]> UnsealAsync(SealedSecret sealedSecret, byte[] associatedData, CancellationToken ct)
        {
            UnsealAttempts++;
            return sealedSecret.KekId == RetiredKek
                ? throw new CryptoConfigurationException($"A secret is wrapped under KEK '{RetiredKek}', which is not loaded.")
                : inner.UnsealAsync(sealedSecret, associatedData, ct);
        }

        public Task<SealedSecret> RewrapAsync(SealedSecret sealedSecret, CancellationToken ct)
            => inner.RewrapAsync(sealedSecret, ct);
    }

    /// <summary>
    /// A cache that cannot be cleared or overwritten, and keeps serving the stale entry: an unreachable
    /// Redis, or a read that started before the batch committed and fills the cache again.
    /// </summary>
    private sealed class StaleCache(EncryptedSecretEnvelope stale) : ISecretCache
    {
        public Task<EncryptedSecretEnvelope?> GetAsync(Guid secretId, CancellationToken ct = default)
            => Task.FromResult<EncryptedSecretEnvelope?>(stale);

        public Task SetAsync(EncryptedSecretEnvelope envelope, CancellationToken ct = default)
            => throw new InvalidOperationException("The cache is unreachable.");

        public Task EvictAsync(Guid secretId, CancellationToken ct = default)
            => throw new InvalidOperationException("The cache is unreachable.");
    }

    private sealed class OneRowRepository(Secret row) : ISecretRepository
    {
        public Task<Secret?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(row.Id == id ? row : null);
        public Task<IReadOnlyList<Secret>> ListActiveAsync(DateTimeOffset asOfUtc, CancellationToken ct) => Task.FromResult<IReadOnlyList<Secret>>([row]);
        public Task RotateAsync(Secret secret, SecretVersion archivedVersion, CancellationToken ct) => Task.CompletedTask;
        public Task<int> CountVersionsAsync(Guid secretId, CancellationToken ct) => Task.FromResult(0);
        public Task<IReadOnlyList<Secret>> ListExpiredAsync(DateTimeOffset asOfUtc, CancellationToken ct) => Task.FromResult<IReadOnlyList<Secret>>([]);
        public Task<IReadOnlyList<Secret>> ListExpiringAsync(DateTimeOffset asOfUtc, DateTimeOffset horizonUtc, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Secret>>([]);
        public Task MarkExpiryNoticeSentAsync(Secret secret, CancellationToken ct) => Task.CompletedTask;
        public Task AddAsync(Secret secret, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(Secret secret, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(Secret secret, CancellationToken ct) => Task.CompletedTask;
        public Task ShredAsync(Secret secret, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<SecretVersion>> ListVersionsAsync(Guid secretId, CancellationToken ct) => Task.FromResult<IReadOnlyList<SecretVersion>>([]);
        public Task<SecretVersion?> FindVersionAsync(Guid secretId, Guid versionId, CancellationToken ct) => Task.FromResult<SecretVersion?>(null);
    }

    private sealed class NoIntrusion : IIntrusionResponseService
    {
        public Task TriggerHoneyTokenAsync(Guid secretId, string secretName, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NullAuditSink : IAuditSink
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubActor : IAuditContext
    {
        public Guid? UserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Username => "dev-user";
        public string? SourceIp => "::1";
    }
}
