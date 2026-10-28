using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence.Configurations;
using EclipsVault.Infrastructure.Persistence.Interceptors;

namespace EclipsVault.Infrastructure.Persistence;

public sealed class AccountChangeCommitter : IAccountChangeCommitter
{
    private readonly EclipsVaultDbContext _context;
    private readonly AuditSaveChangesInterceptor _audit;
    private readonly AccountTransaction _transaction;

    public AccountChangeCommitter(EclipsVaultDbContext context, AuditSaveChangesInterceptor audit, AccountTransaction transaction)
    {
        _context = context;
        _audit = audit;
        _transaction = transaction;
    }

    public Task<bool> CommitAsync(User account, AuditAction action, string details, CancellationToken ct)
        => CommitAsync(account, OwnEvent(account, action, details), _ => Task.CompletedTask, ct);

    public Task<bool> CommitAsync(User account, AuditAction action, string details, Func<CancellationToken, Task> recheck, CancellationToken ct)
        => CommitAsync(account, OwnEvent(account, action, details), recheck, ct);

    public Task<bool> CommitAsync(User account, AuditEntry entry, CancellationToken ct)
        => CommitAsync(account, entry, _ => Task.CompletedTask, ct);

    private async Task<bool> CommitAsync(User account, AuditEntry entry, Func<CancellationToken, Task> recheck, CancellationToken ct)
    {
        if (!_context.ChangeTracker.HasChanges())
        {
            return false;
        }

        // The account's own row joins every change, even one that only touches rows it owns, such as
        // its recovery codes. So two changes made from the same loaded copy conflict on that one row,
        // and the later one is refused instead of both taking effect.
        _context.Entry(account).Property(UserConfiguration.ConcurrencyStamp).CurrentValue = Guid.NewGuid();
        return await _transaction.RunAsync(account.Id, async token =>
        {
            await recheck(token);
            await _audit.SaveOperationAsync(_context, [entry], token);
            return true;
        }, ct);
    }

    private static AuditEntry OwnEvent(User account, AuditAction action, string details) => new()
    {
        Action = action,
        ResourceType = nameof(User),
        ResourceId = account.Id,
        ActorUserId = account.Id,
        ActorUsername = account.Username,
        InheritActorUserId = false,
        Details = details
    };
}
