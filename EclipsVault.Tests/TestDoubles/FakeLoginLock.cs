using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Tests.TestDoubles;

/// <summary>The lock on a backend login: granted, unless another writer holds it.</summary>
public sealed class FakeLoginLock : IManagedPrincipalLock
{
    public bool HeldElsewhere { get; init; }

    /// <summary>Whether the lock is held by the code under test right now.</summary>
    public bool Held { get; private set; }

    public Task<IAsyncDisposable?> TryAcquireAsync(DynamicSecretBackend backend, string principal, CancellationToken ct)
    {
        if (HeldElsewhere)
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        Held = true;
        return Task.FromResult<IAsyncDisposable?>(new Release(this));
    }

    private sealed class Release(FakeLoginLock owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Held = false;
            return ValueTask.CompletedTask;
        }
    }
}
