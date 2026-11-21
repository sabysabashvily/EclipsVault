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

    public void StageAddition(AccountInvitation invitation) => context.AccountInvitations.Add(invitation);
}
