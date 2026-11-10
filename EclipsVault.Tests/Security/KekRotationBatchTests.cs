using System.Security.Cryptography;
using System.Text;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.KeyManagement;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using EclipsVault.Infrastructure.Persistence.Locking;
using EclipsVault.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.Tests.Security;

/// <summary>
/// A rotation pass clears each secret it moves from the secret cache, batch by batch, stops only
/// between batches, and leaves the KMS engine to re-wrap its own rows. Runs the real service against
/// in-memory SQLite; only the database locks, the cache and the KMS engine are stand-ins.
/// </summary>
public sealed class KekRotationBatchTests : IDisposable
{
    private const string RetiredKekId = "kek-retired";
    private const string CurrentKekId = "kek-current";

    private static readonly byte[] RetiredKek = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] CurrentKek = RandomNumberGenerator.GetBytes(32);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AuditChain _chain = new(new UncontendedLocks());
    private readonly AuditSaveChangesInterceptor _audit;
    private readonly RecordingCache _cache;

    public KekRotationBatchTests()
    {
        _connection.Open();
        _audit = new AuditSaveChangesInterceptor(new Actor(), _chain, TimeProvider.System, NullLogger<AuditSaveChangesInterceptor>.Instance);
        _cache = new RecordingCache(RotationsRecordedAsync);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Each_committed_batch_clears_its_secrets_from_the_cache()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        var secrets = await SecretsUnderRetiredKeyAsync(db, count: 501);

        await Rotation(db).RotateAsync(default);

        Assert.Equal([.. secrets.Select(secret => secret.Id).Order()], _cache.Evictions.Select(eviction => eviction.SecretId).Order());
        Assert.Equal(
            [(1, 500), (2, 1)],
            _cache.Evictions.CountBy(eviction => eviction.RotationsRecorded).Select(group => (RotationsRecorded: group.Key, Evictions: group.Value)));
    }

    [Fact]
    public async Task A_caller_that_leaves_mid_batch_stops_the_pass_once_that_batch_has_committed()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        await SecretsUnderRetiredKeyAsync(db, count: 501);
        using var caller = new CancellationTokenSource();
        var leavesDuringFirstRewrap = new CancelsOnRewrap(
            new AesGcmCryptoEngine(new Keys(CurrentKekId, CurrentKek, (RetiredKekId, RetiredKek)), Options.Create(new CryptoOptions())), caller);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Rotation(db, leavesDuringFirstRewrap).RotateAsync(caller.Token));

        Assert.Equal(500, _cache.Evictions.Count);
        Assert.Equal(1, await RotationsRecordedAsync());
        Assert.Equal(
            [(CurrentKekId, 500), (RetiredKekId, 1)],
            await db.Secrets.GroupBy(secret => secret.KekId).OrderByDescending(group => group.Count())
                .Select(group => ValueTuple.Create(group.Key, group.Count())).ToListAsync());
    }

    [Fact]
    public async Task Under_the_KMS_engine_a_pass_sends_each_row_not_under_the_current_key_to_its_rewrap()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        var secret = SecretUnder("vault:eclipsvault:v1", "kms-wrapped");
        db.Secrets.Add(secret);
        await db.SaveChangesAsync();

        var result = await Rotation(db, new KmsRewrap()).RotateAsync(default);

        Assert.Equal(new KekRotationResult(CurrentKekId, 1, 0, 0), result);
        Assert.Equal("vault:eclipsvault:v2", await db.Secrets.Where(row => row.Id == secret.Id).Select(row => row.KekId).SingleAsync());
    }

    [Fact]
    public async Task The_status_counts_rows_a_pass_can_move_apart_from_rows_under_keys_that_are_not_loaded()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        await SecretsUnderRetiredKeyAsync(db, count: 2);
        db.Secrets.Add(SecretUnder("kek-missing", "under-a-missing-key"));
        await db.SaveChangesAsync();

        var status = await Rotation(db).GetStatusAsync(default);

        Assert.Equal((2, 1), (status.AwaitingRotation, status.OnMissingKeys));
    }

    [Fact]
    public async Task Under_the_KMS_engine_the_status_counts_every_row_not_under_the_current_key_as_awaiting_rotation()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        db.Secrets.Add(SecretUnder("vault:eclipsvault:v1", "kms-wrapped"));
        await db.SaveChangesAsync();

        var status = await Rotation(db, new KmsRewrap()).GetStatusAsync(default);

        Assert.Equal((1, 0), (status.AwaitingRotation, status.OnMissingKeys));
    }

    /// <summary>A row whose envelope no test opens: only the id of the key it names matters.</summary>
    private static Secret SecretUnder(string kekId, string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ProjectKey = "ROTATION",
        Ciphertext = [1],
        WrappedDek = Encoding.UTF8.GetBytes("vault:v1:wrapped"),
        KekId = kekId,
        Algorithm = "AES-256-GCM-AAD+VaultTransit",
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    private KekRotationService Rotation(EclipsVaultDbContext db, ICryptoEngine? engine = null) => new(
        db,
        new EngineFactory(engine ?? new AesGcmCryptoEngine(new Keys(CurrentKekId, CurrentKek, (RetiredKekId, RetiredKek)), Options.Create(new CryptoOptions()))),
        new Keys(CurrentKekId, CurrentKek, (RetiredKekId, RetiredKek)),
        _cache,
        _chain,
        _audit,
        new UncontendedLocks());

    private static async Task<List<Secret>> SecretsUnderRetiredKeyAsync(EclipsVaultDbContext db, int count)
    {
        var sealing = new AesGcmCryptoEngine(new Keys(RetiredKekId, RetiredKek), Options.Create(new CryptoOptions()));
        var secrets = new List<Secret>();
        for (var index = 0; index < count; index++)
        {
            var id = Guid.NewGuid();
            var sealedSecret = await sealing.SealAsync(Encoding.UTF8.GetBytes("value"), SecretBinding.ForCurrentValue(id), default);
            secrets.Add(new Secret
            {
                Id = id,
                Name = $"cached-{index:D3}",
                ProjectKey = "ROTATION",
                Ciphertext = sealedSecret.Ciphertext,
                WrappedDek = sealedSecret.WrappedDek,
                KekId = sealedSecret.KekId,
                Algorithm = sealedSecret.Algorithm,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        db.Secrets.AddRange(secrets);
        await db.SaveChangesAsync();
        return secrets;
    }

    private EclipsVaultDbContext NewContext()
        => new(new DbContextOptionsBuilder<EclipsVaultDbContext>().UseSqlite(_connection).AddInterceptors(_audit).Options);

    private async Task<int> RotationsRecordedAsync()
    {
        await using var db = NewContext();
        return await db.AuditLogs.CountAsync(row => row.Action == AuditAction.KekRotated);
    }

    /// <summary>Records each eviction with how many rotation batches had been recorded when it happened.</summary>
    private sealed class RecordingCache(Func<Task<int>> rotationsRecorded) : ISecretCache
    {
        public List<(Guid SecretId, int RotationsRecorded)> Evictions { get; } = [];

        public Task<EncryptedSecretEnvelope?> GetAsync(Guid secretId, CancellationToken ct = default)
            => Task.FromResult<EncryptedSecretEnvelope?>(null);

        public Task SetAsync(EncryptedSecretEnvelope envelope, CancellationToken ct = default) => Task.CompletedTask;

        public async Task EvictAsync(Guid secretId, CancellationToken ct = default)
            => Evictions.Add((secretId, await rotationsRecorded()));
    }

    /// <summary>SQLite has no lock manager, and these tests run one pass on one connection.</summary>
    private sealed class UncontendedLocks : IAuditChainLocker, ISessionLocker
    {
        public Task AcquireAsync(DbContext db, string resource, TimeSpan timeout, CancellationToken ct) => Task.CompletedTask;

        public Task<IAsyncDisposable?> TryAcquireAsync(DbContext db, string resource, CancellationToken ct)
            => Task.FromResult<IAsyncDisposable?>(new Released());

        private sealed class Released : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Keys(string currentId, byte[] current, params (string Id, byte[] Key)[] retired) : IKekProvider
    {
        private readonly Dictionary<string, byte[]> _keys = new[] { (Id: currentId, Key: current) }.Concat(retired)
            .ToDictionary(key => key.Id, key => key.Key, StringComparer.Ordinal);

        public string CurrentKekId => currentId;

        public byte[] CurrentKek => current;

        public IReadOnlyList<string> KnownKekIds => [.. _keys.Keys];

        public byte[] ResolveKek(string kekId) => _keys[kekId];
    }

    /// <summary>Re-wraps as the KMS engine does: under its own latest key version, with an id of its own.</summary>
    private sealed class KmsRewrap : ICryptoEngine
    {
        public string EngineId => VaultTransitCryptoEngine.EngineName;

        public Task<SealedSecret> SealAsync(byte[] plaintext, byte[] associatedData, CancellationToken ct) => throw new NotSupportedException();

        public Task<byte[]> UnsealAsync(SealedSecret sealedSecret, byte[] associatedData, CancellationToken ct) => throw new NotSupportedException();

        public Task<SealedSecret> RewrapAsync(SealedSecret sealedSecret, CancellationToken ct)
            => Task.FromResult(sealedSecret with { WrappedDek = Encoding.UTF8.GetBytes("vault:v2:wrapped"), KekId = "vault:eclipsvault:v2" });
    }

    /// <summary>The real engine, whose first re-wrap is where the caller leaves.</summary>
    private sealed class CancelsOnRewrap(ICryptoEngine engine, CancellationTokenSource caller) : ICryptoEngine
    {
        public string EngineId => engine.EngineId;

        public Task<SealedSecret> SealAsync(byte[] plaintext, byte[] associatedData, CancellationToken ct) => engine.SealAsync(plaintext, associatedData, ct);

        public Task<byte[]> UnsealAsync(SealedSecret sealedSecret, byte[] associatedData, CancellationToken ct) => engine.UnsealAsync(sealedSecret, associatedData, ct);

        public Task<SealedSecret> RewrapAsync(SealedSecret sealedSecret, CancellationToken ct)
        {
            caller.Cancel();
            return engine.RewrapAsync(sealedSecret, ct);
        }
    }

    private sealed class EngineFactory(ICryptoEngine engine) : ICryptoEngineFactory
    {
        public ICryptoEngine Create() => engine;
    }

    private sealed class Actor : IAuditContext
    {
        public Guid? UserId => null;

        public string? Username => "rotation-test";

        public string? SourceIp => "::1";
    }
}
