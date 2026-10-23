using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Mfa;

/// <summary>
/// Issues single-use MFA recovery codes. A generation replaces any codes the user held,
/// persists each as a salted Argon2id hash, and returns the plaintext once for the user
/// to record. Every generation commits together with its audit event.
/// </summary>
public sealed class MfaRecoveryService : IMfaRecoveryService
{
    /// <summary>Number of codes issued per set — matches common practice (GitHub, Google).</summary>
    public const int CodesPerSet = 10;

    private readonly IUserRepository _users;
    private readonly IMfaRecoveryCodeRepository _codes;
    private readonly IPasswordHasher _hasher;
    private readonly IAccountChangeCommitter _changes;
    private readonly TimeProvider _clock;

    public MfaRecoveryService(
        IUserRepository users,
        IMfaRecoveryCodeRepository codes,
        IPasswordHasher hasher,
        IAccountChangeCommitter changes,
        TimeProvider clock)
    {
        _users = users;
        _codes = codes;
        _hasher = hasher;
        _changes = changes;
        _clock = clock;
    }

    public Task<int> CountRemainingAsync(Guid userId, CancellationToken ct)
        => _codes.CountUnusedAsync(userId, ct);

    public async Task<IReadOnlyList<string>> GenerateAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new InvalidOperationException($"User '{userId}' was not found.");

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

        // Committed against the account as loaded above, so a replacement saved by another request in
        // the meantime refuses this one rather than leaving both new sets valid.
        await _codes.StageReplacementAsync(userId, entities, ct);
        await _changes.CommitAsync(
            user, AuditAction.RecoveryCodesGenerated,
            $"{CodesPerSet} single-use MFA recovery codes generated; any previous codes invalidated", ct);

        return display;
    }
}
