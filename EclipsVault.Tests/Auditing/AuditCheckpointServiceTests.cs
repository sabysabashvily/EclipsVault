using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Licensing;
using EclipsVault.Infrastructure.Auditing;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Locking;
using EclipsVault.Tests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.Tests.Auditing;

public class AuditCheckpointServiceTests
{
    private sealed class FakeSigner : IAuditCheckpointSigner
    {
        public byte[] Sign(byte[] canonical) => [];
        public byte[] PublicKeySpki => [];
        public string KeyId => "test-key";
    }

    private sealed class NoOpAuditSink : IAuditSink
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class UnusedLocks : ISessionLocker
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(DbContext db, string resource, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class GrantedLocks : ISessionLocker
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(DbContext db, string resource, CancellationToken ct) => Task.FromResult<IAsyncDisposable?>(new Held());

        private sealed class Held : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Creating_a_checkpoint_records_premium_usage()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<EclipsVaultDbContext>(o => o.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.Database.EnsureCreatedAsync();

        var usage = new RecordingPremiumFeatureUsage();
        var service = new AuditCheckpointService(db, new FakeSigner(), new NoOpAuditSink(), TimeProvider.System, usage, new UnusedLocks());

        var result = await service.CreateCheckpointAsync(CancellationToken.None);

        Assert.Null(result); // empty chain → nothing to sign
        Assert.Equal(LicenseFeatures.AuditAttestation, Assert.Single(usage.Recorded));
    }

    [Fact]
    public async Task Exporting_a_bundle_records_premium_usage()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<EclipsVaultDbContext>(o => o.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.Database.EnsureCreatedAsync();

        var usage = new RecordingPremiumFeatureUsage();
        var service = new AuditCheckpointService(db, new FakeSigner(), new NoOpAuditSink(), TimeProvider.System, usage, new GrantedLocks());

        await using var export = await service.ExportAsync(CancellationToken.None);

        Assert.Equal(0, export.Checkpoint.Sequence); // empty chain → signed at genesis
        Assert.Equal(LicenseFeatures.AuditAttestation, Assert.Single(usage.Recorded));
    }
}
