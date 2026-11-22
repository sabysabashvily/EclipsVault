using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// A cluster-wide lock on one backend login, taken by every write to the value of a secret bound to it,
/// so one writer's put-back can never undo another writer's change.
/// </summary>
public interface IManagedPrincipalLock
{
    /// <summary>
    /// Takes the lock without waiting. Returns the handle that releases it, or null when another writer
    /// holds it.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(DynamicSecretBackend backend, string principal, CancellationToken ct);
}
