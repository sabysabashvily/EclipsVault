using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Abstractions;

public interface IAccountInvitationRepository
{
    Task<AccountInvitation?> FindByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Every account's invitation, read-only, for the Users list.</summary>
    Task<IReadOnlyList<AccountInvitation>> ListAsync(CancellationToken ct);

    Task<AccountInvitation?> FindByTokenHashAsync(byte[] tokenHash, CancellationToken ct);

    /// <summary>
    /// Stores <paramref name="seed"/> as the invitation's pending authenticator seed unless one is already
    /// stored, in one conditional write, and only while the invitation still holds <paramref name="tokenHash"/>
    /// unspent and unexpired.
    /// </summary>
    /// <returns>The seed the invitation now holds, or null when the token is no longer usable.</returns>
    Task<string?> StorePendingSecretOnceAsync(Guid invitationId, byte[] tokenHash, string seed, DateTimeOffset nowUtc, CancellationToken ct);

    /// <summary>Stages adding <paramref name="invitation"/>, to be saved through <see cref="IAccountChangeCommitter"/>.</summary>
    void StageAddition(AccountInvitation invitation);

    /// <summary>
    /// Stages a resend's or address replacement's reset of <paramref name="invitation"/> so that its pending seed
    /// and spent marker are written even when they look unchanged: an inspection may have stored a seed since it was read.
    /// </summary>
    void StageReset(AccountInvitation invitation);
}
