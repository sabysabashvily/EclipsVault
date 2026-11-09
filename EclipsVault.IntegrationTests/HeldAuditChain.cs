using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The vault's audit chain, held by a transaction of the test's own. Every audited commit waits for
/// it, so a test can let other work read, queue up behind the chain, and then commit in a known order.
/// Services resolved from <see cref="Services"/> share the holding transaction.
/// </summary>
internal sealed class HeldAuditChain : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;
    private readonly EclipsVaultDbContext _db;
    private readonly IDbContextTransaction _transaction;

    private HeldAuditChain(AsyncServiceScope scope, EclipsVaultDbContext db, IDbContextTransaction transaction)
    {
        _scope = scope;
        _db = db;
        _transaction = transaction;
    }

    public IServiceProvider Services => _scope.ServiceProvider;

    public static async Task<HeldAuditChain> TakeAsync(VaultFixture vault)
    {
        var scope = vault.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<AuditChain>().AcquireAsync(db, default);
        return new HeldAuditChain(scope, db, transaction);
    }

    public async Task WaitForQueuedWritersAsync(int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var queuedChainWriters = _db.Database.IsNpgsql()
            ? ConcurrentShareRevocationTests.PostgresQueuedChainWriters
            : ConcurrentShareRevocationTests.SqlServerQueuedChainWriters;
        while (await _db.Database.SqlQueryRaw<int>(queuedChainWriters).SingleAsync(deadline.Token) < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
        }
    }

    /// <summary>Commits what the holder wrote and releases the chain.</summary>
    public Task CommitAsync() => _transaction.CommitAsync();

    /// <summary>Releases the chain, rolling back anything the holder did not commit.</summary>
    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _scope.DisposeAsync();
    }
}
