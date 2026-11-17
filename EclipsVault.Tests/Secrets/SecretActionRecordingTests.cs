using System.Text;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using EclipsVault.Infrastructure.Persistence.Locking;
using EclipsVault.Infrastructure.Persistence.Repositories;
using EclipsVault.Tests.Fakes;
using EclipsVault.Tests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.Tests.Secrets;

/// <summary>
/// Each secret action records only its own event, committed with the change it describes. Runs the
/// real service, repository and audit interceptor against in-memory SQLite; the crypto engine, cache,
/// backend and licence gate are stand-ins, and standalone audit writes are only recorded.
/// </summary>
public sealed class SecretActionRecordingTests : IDisposable
{
    private const string Principal = "app_recording";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AuditChain _chain = new(new UncontendedLocks());
    private readonly AuditSaveChangesInterceptor _interceptor;
    private readonly RecordingAuditSink _standalone = new();

    public SecretActionRecordingTests()
    {
        _interceptor = new(new Actor(), _chain, TimeProvider.System, NullLogger<AuditSaveChangesInterceptor>.Instance);
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Looking_a_secret_up_by_id_records_nothing()
    {
        await using var db = await NewDatabaseAsync();
        var secretId = await Service(db).CreateAsync(NewSecret("it-lookup"), default);

        await Service(db).GetDetailsAsync(secretId, default);

        Assert.Equal([AuditAction.SecretCreated], await CommittedActionsAsync(db, secretId));
        Assert.Empty(_standalone.Entries);
    }

    [Fact]
    public async Task Restoring_a_version_commits_one_restore_row_with_the_change()
    {
        await using var db = await NewDatabaseAsync();
        var secrets = Service(db);
        var secretId = await secrets.CreateAsync(NewSecret("it-restore"), default);
        await secrets.RotateAsync(secretId, "second value", changeNote: null, renewTtlDays: null, default);
        var version = Assert.Single(await secrets.ListVersionsAsync(secretId, default));

        await secrets.RestoreVersionAsync(secretId, version.Id, default);

        Assert.Equal(
            [AuditAction.SecretCreated, AuditAction.SecretUpdated, AuditAction.SecretVersionRestored],
            await CommittedActionsAsync(db, secretId));
        Assert.Empty(_standalone.Entries);
    }

    [Fact]
    public async Task A_write_of_a_value_read_before_another_change_committed_saves_nothing_and_keeps_the_stored_value()
    {
        await using var db = await NewDatabaseAsync();
        var secretId = await Service(db).CreateAsync(NewSecret("it-stale-write"), default);
        await using var staleRequest = await NewDatabaseAsync();
        var stale = await staleRequest.Secrets.SingleAsync(secret => secret.Id == secretId);
        var readWrappedDek = stale.WrappedDek;
        await Service(db).RotateAsync(secretId, "second value", changeNote: null, renewTtlDays: null, default);
        stale.Name = "it-stale-write-overwritten";

        await Assert.ThrowsAsync<Core.Domain.Exceptions.SecretChangedException>(() => new SecretRepository(staleRequest, _interceptor, _chain)
            .RotateAsync(stale, readWrappedDek, new SecretVersion { Id = Guid.NewGuid(), SecretId = secretId }, AuditAction.SecretUpdated, default));

        Assert.Equal("it-stale-write", stale.Name);
        Assert.Equal([AuditAction.SecretCreated, AuditAction.SecretUpdated], await CommittedActionsAsync(db, secretId));
        Assert.Equal([1], await db.SecretVersions.Where(version => version.SecretId == secretId).Select(version => version.VersionNumber).ToListAsync());
    }

    [Fact]
    public async Task Rotating_upstream_commits_one_upstream_rotation_row_with_the_change()
    {
        await using var db = await NewDatabaseAsync();
        var secretId = await AddManagedSecretAsync(db);

        await Service(db).RotateManagedAsync(secretId, renewTtlDays: null, default);

        Assert.Equal([AuditAction.SecretCreated, AuditAction.SecretUpstreamRotated], await CommittedActionsAsync(db, secretId));
        Assert.Empty(_standalone.Entries);
    }

    private async Task<EclipsVaultDbContext> NewDatabaseAsync()
    {
        var db = new EclipsVaultDbContext(
            new DbContextOptionsBuilder<EclipsVaultDbContext>().UseSqlite(_connection).AddInterceptors(_interceptor).Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private SecretService Service(EclipsVaultDbContext db)
        => new(new SecretRepository(db, _interceptor, _chain), new FakeCryptoEngine(), new NoCache(), new NoIntrusion(), _standalone, new Actor(),
            [new AcceptingBackend()], TimeProvider.System, new RecordingPremiumFeatureUsage(), new FakeLoginLock());

    private static CreateSecretRequest NewSecret(string name)
        => new(name, "first value", "RECORDING", SecretEnvironment.Development, SensitivityLevel.Internal, TtlDays: 0);

    /// <summary>A managed secret is bound to its principal in the database: no screen makes one.</summary>
    private static async Task<Guid> AddManagedSecretAsync(EclipsVaultDbContext db)
    {
        var id = Guid.NewGuid();
        var sealedSecret = await new FakeCryptoEngine().SealAsync(
            Encoding.UTF8.GetBytes("current password"), SecretBinding.ForCurrentValue(id), default);
        db.Secrets.Add(new Secret
        {
            Id = id,
            Name = "it-managed",
            ProjectKey = "RECORDING",
            Ciphertext = sealedSecret.Ciphertext,
            WrappedDek = sealedSecret.WrappedDek,
            KekId = sealedSecret.KekId,
            Algorithm = sealedSecret.Algorithm,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RotationBackend = DynamicSecretBackend.SqlServer,
            RotationPrincipal = Principal,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<List<AuditAction>> CommittedActionsAsync(EclipsVaultDbContext db, Guid secretId)
        => await db.AuditLogs.AsNoTracking().Where(row => row.ResourceId == secretId)
            .OrderBy(row => row.Sequence).Select(row => row.Action).ToListAsync();

    private sealed class RecordingAuditSink : IAuditSink
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class AcceptingBackend : IManagedSecretBackend
    {
        public DynamicSecretBackend Backend => DynamicSecretBackend.SqlServer;

        public Task RotatePrincipalAsync(string principal, string newPassword, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoCache : ISecretCache
    {
        public Task<EncryptedSecretEnvelope?> GetAsync(Guid secretId, CancellationToken ct = default)
            => Task.FromResult<EncryptedSecretEnvelope?>(null);

        public Task SetAsync(EncryptedSecretEnvelope envelope, CancellationToken ct = default) => Task.CompletedTask;

        public Task EvictAsync(Guid secretId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoIntrusion : IIntrusionResponseService
    {
        public Task TriggerHoneyTokenAsync(Guid secretId, string secretName, CancellationToken ct)
            => throw new InvalidOperationException("No decoy is involved in these tests.");
    }

    /// <summary>SQLite has no lock manager, and these tests write on one connection.</summary>
    private sealed class UncontendedLocks : IAuditChainLocker
    {
        public Task AcquireAsync(DbContext db, string resource, TimeSpan timeout, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Actor : IAuditContext
    {
        public Guid? UserId => null;

        public string? Username => "recording-test";

        public string? SourceIp => "::1";
    }
}
