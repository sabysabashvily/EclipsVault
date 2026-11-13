using System.Security.Cryptography;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Auditing;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using EclipsVault.Infrastructure.Persistence.Locking;
using EclipsVault.Tests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.Auditing;

/// <summary>
/// A Linux clock reads to 100 ns, and PostgreSQL stores a time to the microsecond. An audit row's hash
/// and a checkpoint's signature must cover the exact microsecond time that can be stored unchanged.
/// </summary>
public class AuditTimePrecisionTests
{
    /// <summary>12:00:00.1234567, a reading with a digit below the microsecond, as a Linux clock gives.</summary>
    private static readonly DateTimeOffset ClockReading = new DateTimeOffset(2026, 11, 13, 12, 0, 0, 123, 456, TimeSpan.Zero).AddTicks(7);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Signer(ECDsa key) : IAuditCheckpointSigner
    {
        public byte[] Sign(byte[] canonical) => key.SignData(canonical, HashAlgorithmName.SHA256);
        public byte[] PublicKeySpki => key.ExportSubjectPublicKeyInfo();
        public string KeyId => AuditSigningKeyId.For(PublicKeySpki);
    }

    private sealed class Actor : IAuditContext
    {
        public Guid? UserId => null;
        public string? Username => "alice";
        public string? SourceIp => "203.0.113.5";
    }

    private sealed class NoOpAuditSink : IAuditSink
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class UnusedLocks : ISessionLocker
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(DbContext db, string resource, CancellationToken ct) => throw new NotSupportedException();
    }

    // This fixture has one writer on one SQLite connection; provider lock behavior is covered by integration tests.
    private sealed class UncontendedChainLocks : IAuditChainLocker
    {
        public Task AcquireAsync(DbContext db, string resource, TimeSpan timeout, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task An_audit_row_keeps_its_microsecond_timestamp_verifiable()
    {
        await using var vault = await Vault.OpenAsync();
        using var committer = new AuditGroupCommitter(vault.Scopes, NullLogger<AuditGroupCommitter>.Instance);
        await committer.StartAsync(CancellationToken.None);
        var sink = new AuditSink(committer, new Actor(), new FixedClock(ClockReading), NullLogger<AuditSink>.Instance);

        await sink.WriteAsync(new AuditEntry { Action = AuditAction.SecretRevealed, ResourceType = "Secret" }, CancellationToken.None);
        await committer.StopAsync(CancellationToken.None);

        var row = await vault.Db.AuditLogs.SingleAsync();
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 12, 0, 0, 123, 456, TimeSpan.Zero), row.TimestampUtc);
        Assert.Equal(row.EntryHash, AuditRowHasher.Compute(row, AuditRowHasher.GenesisHash));
    }

    [Fact]
    public async Task A_checkpoint_signs_its_whole_microsecond_timestamp()
    {
        await using var vault = await Vault.OpenAsync();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var service = new AuditCheckpointService(
            vault.Db, new Signer(key), new NoOpAuditSink(), new FixedClock(ClockReading), new RecordingPremiumFeatureUsage(), new UnusedLocks());

        var checkpoint = (await service.ExportAsync(CancellationToken.None)).Checkpoint;

        var expectedTime = new DateTimeOffset(2026, 11, 13, 12, 0, 0, 123, 456, TimeSpan.Zero);
        Assert.Equal(expectedTime, checkpoint.CreatedAtUtc);
        var canonical = AuditCheckpointCanonical.Bytes(checkpoint.Sequence, checkpoint.ChainHeadHash, expectedTime);
        Assert.True(key.VerifyData(canonical, checkpoint.Signature, HashAlgorithmName.SHA256));
    }

    private sealed class Vault : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private Vault(SqliteConnection connection, ServiceProvider provider)
        {
            _connection = connection;
            _provider = provider;
            _scope = provider.CreateAsyncScope();
        }

        public IServiceScopeFactory Scopes => _provider.GetRequiredService<IServiceScopeFactory>();

        public EclipsVaultDbContext Db => _scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();

        public static async Task<Vault> OpenAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddScoped(_ => new AuditSaveChangesInterceptor(
                new Actor(), new AuditChain(new UncontendedChainLocks()), new FixedClock(ClockReading),
                NullLogger<AuditSaveChangesInterceptor>.Instance));
            services.AddDbContext<EclipsVaultDbContext>((provider, options) => options
                .UseSqlite(connection)
                .AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>()));
            var vault = new Vault(connection, services.BuildServiceProvider());
            await vault.Db.Database.EnsureCreatedAsync();
            return vault;
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
