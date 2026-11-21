using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace EclipsVault.Tests.Auditing;

/// <summary>
/// A batch's transaction holds the audit chain. A commit that fails must still end it: left open, it
/// keeps every other writer waiting, and a read on the same connection would see rows that never
/// committed as though they had.
/// </summary>
public sealed class AuditChainCommitTests
{
    [Fact]
    public async Task A_commit_that_fails_still_ends_its_transaction()
    {
        var transaction = new FailingCommit();

        await Assert.ThrowsAsync<OperationCanceledException>(() => AuditChain.CommitAsync(new AuditBatch(transaction), default));

        Assert.True(transaction.Ended);
    }

    /// <summary>A transaction whose commit is cancelled, as when the caller leaves before it lands.</summary>
    private sealed class FailingCommit : IDbContextTransaction
    {
        public bool Ended { get; private set; }

        public Guid TransactionId { get; } = Guid.NewGuid();

        public void Commit() => throw new NotSupportedException();

        public Task CommitAsync(CancellationToken cancellationToken = default) => throw new OperationCanceledException();

        public void Rollback() => throw new NotSupportedException();

        public Task RollbackAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Dispose() => Ended = true;

        public ValueTask DisposeAsync()
        {
            Ended = true;
            return ValueTask.CompletedTask;
        }
    }
}
