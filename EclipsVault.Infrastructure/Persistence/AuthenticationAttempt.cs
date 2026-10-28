using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence.Configurations;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence;

public sealed class AuthenticationAttempt(
    EclipsVaultDbContext context, AccountTransaction transaction, AuditSaveChangesInterceptor audit, AuditChain chain) : IAuthenticationAttempt
{
    private List<AuditEntry>? _events;

    public Task<T> RunAsync<T>(User account, Func<CancellationToken, Task<T>> attempt, CancellationToken ct)
        => transaction.RunAsync(account.Id, async token =>
        {
            // Sharing holds the chain while its foreign-key check reads this account. Take the
            // chain before any account write so neither operation waits for a lock the other holds.
            await chain.AcquireAsync(context, token);
            await context.Entry(account).ReloadAsync(token);
            _events = [];
            try
            {
                var result = await attempt(token);
                var accountEntry = context.Entry(account);
                if (accountEntry.State == EntityState.Modified)
                {
                    accountEntry.Property(UserConfiguration.ConcurrencyStamp).CurrentValue = Guid.NewGuid();
                }

                await audit.SaveOperationAsync(context, _events, token);
                return result;
            }
            finally
            {
                _events = null;
            }
        }, ct);

    public void Record(AuditEntry entry)
        => (_events ?? throw new InvalidOperationException("An authentication event needs a running attempt.")).Add(entry);
}
