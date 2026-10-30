using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Distributed;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EclipsVault.Infrastructure.Security.WebAuthn;

public sealed class RedisPasskeyCeremonyStore(IConnectionMultiplexer redis, IOptions<RedisOptions> options) : IPasskeyCeremonyStore
{
    public async Task ReplaceAsync(string sessionId, PasskeyCeremonyPurpose purpose, string state, CancellationToken ct)
        => await redis.GetDatabase().StringSetAsync(Key(sessionId, purpose), state, ProfileOperationLimits.CeremonyLifetime);

    public async Task<string?> TakeAsync(string sessionId, PasskeyCeremonyPurpose purpose, CancellationToken ct)
        => (string?)await redis.GetDatabase().StringGetDeleteAsync(Key(sessionId, purpose));

    private RedisKey Key(string sessionId, PasskeyCeremonyPurpose purpose)
        => $"{options.Value.SessionKeyPrefix}:passkey-ceremony:{sessionId}:{purpose}";
}
