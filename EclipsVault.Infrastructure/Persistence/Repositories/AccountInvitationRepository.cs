using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class AccountInvitationRepository(EclipsVaultDbContext context) : IAccountInvitationRepository
{
    public Task<AccountInvitation?> FindByUserIdAsync(Guid userId, CancellationToken ct)
        => context.AccountInvitations.FirstOrDefaultAsync(i => i.UserId == userId, ct);

    public Task<AccountInvitation?> FindByTokenHashAsync(byte[] tokenHash, CancellationToken ct)
        => context.AccountInvitations.Include(i => i.User)
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

    public async Task<string?> StorePendingSecretOnceAsync(Guid invitationId, byte[] tokenHash, string seed, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var usable = context.AccountInvitations.Where(i => i.Id == invitationId && i.TokenHash == tokenHash
            && i.SpentAtUtc == null && i.ExpiresAtUtc > nowUtc);
        await usable.Where(i => i.PendingTotpSecret == null)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.PendingTotpSecret, seed), ct);
        return await usable.AsNoTracking().Select(i => i.PendingTotpSecret).SingleOrDefaultAsync(ct);
    }

    public void StageAddition(AccountInvitation invitation) => context.AccountInvitations.Add(invitation);
}
