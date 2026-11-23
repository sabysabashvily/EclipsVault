using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Mfa;

/// <summary>
/// Issues single-use MFA recovery codes. A new set replaces any codes the user held, is
/// staged as salted Argon2id hashes for the caller's change to commit with its audit event,
/// and its plaintext is returned once for the user to record.
/// </summary>
public sealed class MfaRecoveryService : IMfaRecoveryService
{
    /// <summary>Number of codes issued per set — matches common practice (GitHub, Google).</summary>
    public const int CodesPerSet = 10;

    private readonly IMfaRecoveryCodeRepository _codes;
    private readonly IPasswordHasher _hasher;
    private readonly TimeProvider _clock;

    public MfaRecoveryService(IMfaRecoveryCodeRepository codes, IPasswordHasher hasher, TimeProvider clock)
    {
        _codes = codes;
        _hasher = hasher;
        _clock = clock;
    }

    public Task<int> CountRemainingAsync(Guid userId, CancellationToken ct)
        => _codes.CountUnusedAsync(userId, ct);

    public async Task<IReadOnlyList<string>> StageNewSetAsync(Guid userId, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var display = new List<string>(CodesPerSet);
        var entities = new List<MfaRecoveryCode>(CodesPerSet);

        for (var i = 0; i < CodesPerSet; i++)
        {
            var code = RecoveryCodeFormat.NewCode();
            display.Add(code);

            var hashed = _hasher.Hash(RecoveryCodeFormat.Normalize(code));
            entities.Add(new MfaRecoveryCode
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CodeHash = hashed.Hash,
                Salt = hashed.Salt,
                CreatedAtUtc = now
            });
        }

        await _codes.StageReplacementAsync(userId, entities, ct);
        return display;
    }
}
