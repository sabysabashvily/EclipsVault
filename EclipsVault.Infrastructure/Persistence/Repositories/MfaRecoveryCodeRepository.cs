using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class MfaRecoveryCodeRepository : IMfaRecoveryCodeRepository
{
    private readonly EclipsVaultDbContext _context;

    public MfaRecoveryCodeRepository(EclipsVaultDbContext context) => _context = context;

    public async Task<IReadOnlyList<MfaRecoveryCode>> ListUnusedAsync(Guid userId, CancellationToken ct)
        => await _context.MfaRecoveryCodes
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.UsedAtUtc == null)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<int> CountUnusedAsync(Guid userId, CancellationToken ct)
        => _context.MfaRecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAtUtc == null, ct);

    public async Task StageReplacementAsync(Guid userId, IReadOnlyList<MfaRecoveryCode> codes, CancellationToken ct)
    {
        await StageRemovalOfAllAsync(userId, ct);
        _context.MfaRecoveryCodes.AddRange(codes);
    }

    public async Task StageRemovalOfAllAsync(Guid userId, CancellationToken ct)
    {
        var existing = await _context.MfaRecoveryCodes.Where(c => c.UserId == userId).ToListAsync(ct);
        _context.MfaRecoveryCodes.RemoveRange(existing);
    }

    public async Task<bool> TryRedeemAsync(MfaRecoveryCode code, DateTimeOffset usedAtUtc, CancellationToken ct)
    {
        var redeemed = await _context.MfaRecoveryCodes
            .Where(c => c.Id == code.Id && c.UsedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UsedAtUtc, usedAtUtc), ct) == 1;
        if (redeemed)
        {
            code.UsedAtUtc = usedAtUtc;
        }

        return redeemed;
    }
}
