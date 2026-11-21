using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Abstractions;

public interface IAccountInvitationRepository
{
    Task<AccountInvitation?> FindByUserIdAsync(Guid userId, CancellationToken ct);

    Task<AccountInvitation?> FindByTokenHashAsync(byte[] tokenHash, CancellationToken ct);

    /// <summary>Stages adding <paramref name="invitation"/>, to be saved through <see cref="IAccountChangeCommitter"/>.</summary>
    void StageAddition(AccountInvitation invitation);
}
