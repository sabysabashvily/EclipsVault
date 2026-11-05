using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using EclipsVault.Infrastructure.Persistence.Locking;
using EclipsVault.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EclipsVault.Infrastructure.Persistence;

public sealed class ServiceAccountChanges(EclipsVaultDbContext context, AuditSaveChangesInterceptor audit, IAuditChainLocker locks)
    : IServiceAccountChanges
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);

    public async Task<IServiceAccountChange> BeginAsync(Guid serviceAccountId, CancellationToken ct)
    {
        var change = new Change(context, audit, await context.Database.BeginTransactionAsync(ct));
        try
        {
            // Taken before the audit chain's lock, which the commit takes, as every holder of both does.
            await locks.AcquireAsync(context, $"EclipsVault:ServiceAccount:{serviceAccountId:D}", LockTimeout, ct);
            return change;
        }
        catch (Exception failure)
        {
            await change.DisposeAsync();
            if (failure is OperationCanceledException)
            {
                throw;
            }

            // Reported as account changes report it: the change could not be stored with its audit event.
            throw new AuditWriteFailedException("The service account's lock could not be taken, so nothing was changed.", failure);
        }
    }

    private sealed class Change(EclipsVaultDbContext context, AuditSaveChangesInterceptor audit, IDbContextTransaction transaction)
        : IServiceAccountChange
    {
        private bool _committed;

        public async Task CommitAsync(AuditEntry entry, CancellationToken ct)
        {
            try
            {
                await audit.SaveOperationAsync(context, [entry], ct);
                await transaction.CommitAsync(ct);
                _committed = true;
            }
            catch (DbUpdateException failure) when (UniqueIndex.IsViolatedBy(failure, ServiceAccountRepository.UniqueNameIndex))
            {
                throw new ServiceAccountNameTakenException();
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                throw new AuditWriteFailedException("The service-account change and its audit event could not be stored.", failure);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                // Whatever the change staged must not stay on the request's context, where the next save
                // anywhere in the request would commit it without its audit event.
                context.ChangeTracker.Clear();
            }

            // Disposing a transaction that was not committed rolls it back.
            await transaction.DisposeAsync();
        }
    }
}
