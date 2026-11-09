using System.Security.Cryptography;
using System.Text;
using EclipsVault.Core.Application.Abstractions;
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
/// A rotation pass clears each secret it moves from the secret cache, batch by batch, and stops only
/// between batches. Runs the real service against in-memory SQLite; only the database locks and the
/// cache are stand-ins.
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
        _cache.OnEviction = caller.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Rotation(db).RotateAsync(caller.Token));

        Assert.Equal(500, _cache.Evictions.Count);
        Assert.Equal(
            [(CurrentKekId, 500), (RetiredKekId, 1)],
            await db.Secrets.GroupBy(secret => secret.KekId).OrderByDescending(group => group.Count())
                .Select(group => ValueTuple.Create(group.Key, group.Count())).ToListAsync());
    }

    private KekRotationService Rotation(EclipsVaultDbContext db) => new(
        db,
        new EngineFactory(new AesGcmCryptoEngine(new Keys(CurrentKekId, CurrentKek, (RetiredKekId, RetiredKek)), Options.Create(new CryptoOptions()))),
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

        public Action OnEviction { get; set; } = () => { };

        public Task<EncryptedSecretEnvelope?> GetAsync(Guid secretId, CancellationToken ct = default)
            => Task.FromResult<EncryptedSecretEnvelope?>(null);

        public Task SetAsync(EncryptedSecretEnvelope envelope, CancellationToken ct = default) => Task.CompletedTask;

        public async Task EvictAsync(Guid secretId, CancellationToken ct = default)
        {
            OnEviction();
            Evictions.Add((secretId, await rotationsRecorded()));
        }
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
