using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace EclipsVault.Infrastructure.Security.WebAuthn;

public sealed class MemoryPasskeyCeremonyStore(IMemoryCache cache, TimeProvider clock) : IPasskeyCeremonyStore
{
    private readonly object _gate = new();

    public Task ReplaceAsync(string sessionId, PasskeyCeremonyPurpose purpose, string state, CancellationToken ct)
    {
        lock (_gate)
        {
            cache.Set(Key(sessionId, purpose), new Ceremony(state, clock.GetUtcNow() + ProfileOperationLimits.CeremonyLifetime),
                ProfileOperationLimits.CeremonyLifetime);
        }
        return Task.CompletedTask;
    }

    public Task<string?> TakeAsync(string sessionId, PasskeyCeremonyPurpose purpose, CancellationToken ct)
    {
        lock (_gate)
        {
            var key = Key(sessionId, purpose);
            var ceremony = cache.Get<Ceremony>(key);
            cache.Remove(key);
            return Task.FromResult(ceremony is not null && ceremony.ExpiresAtUtc > clock.GetUtcNow() ? ceremony.State : null);
        }
    }

    private static string Key(string sessionId, PasskeyCeremonyPurpose purpose) => $"passkey-ceremony:{sessionId}:{purpose}";

    private sealed record Ceremony(string State, DateTimeOffset ExpiresAtUtc);
}
