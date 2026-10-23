using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence.Locking;
using EclipsVault.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EclipsVault.Infrastructure.Persistence;

/// <summary>
/// Runs one account operation in a database transaction that holds the account's lock, so
/// operations on the same account take effect one at a time. A failure leaves nothing staged and
/// is reported as the domain outcome a caller can act on: the account changed, the email is taken,
/// or the operation and its audit events could not be stored.
/// </summary>
public sealed class AccountTransaction(EclipsVaultDbContext context, IAuditChainLocker locks)
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    public async Task<T> RunAsync<T>(Guid accountId, Func<CancellationToken, Task<T>> change, CancellationToken ct)
    {
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await context.Database.BeginTransactionAsync(ct);
            // Every account operation takes its account lock before the chain lock. Reversing that
            // order in one path can deadlock a reset against redemption of one of its recovery codes.
            await locks.AcquireAsync(context, $"EclipsVault:Account:{accountId:D}", LockTimeout, ct);
            var result = await change(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception failure)
        {
            Exception cause = failure;
            try
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
            }
            catch (Exception rollbackFailure)
            {
                cause = new AggregateException(failure, rollbackFailure);
            }
            finally
            {
                // ExecuteUpdate bypasses the tracker, and an earlier save may have accepted values
                // in this transaction. Cleanup must also run if starting or rolling back failed.
                context.ChangeTracker.Clear();
            }

            if (failure is DbUpdateConcurrencyException)
            {
                throw new AccountChangedException();
            }

            if (failure is DbUpdateException update && UserRepository.ViolatesUniqueEmail(update))
            {
                throw new EmailAddressTakenException();
            }

            if (failure is OperationCanceledException or AccountChangedException or EmailAddressTakenException)
            {
                throw;
            }

            throw new AuditWriteFailedException("The account operation and its audit events could not be stored.", cause);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
