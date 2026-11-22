using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Infrastructure.Persistence.Locking;

/// <summary>
/// The lock on one backend login, owned by this request's database session so it lasts across the
/// write's own transaction and a put-back after it, and is released if the process dies.
/// </summary>
public sealed class ManagedPrincipalLock(EclipsVaultDbContext db, ISessionLocker locker) : IManagedPrincipalLock
{
    public Task<IAsyncDisposable?> TryAcquireAsync(DynamicSecretBackend backend, string principal, CancellationToken ct)
        => locker.TryAcquireAsync(db, $"EclipsVault:ManagedPrincipal:{backend}:{principal}", ct);
}
