using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Persistence for a user's single-use MFA recovery codes. Replacing and removing codes are staged:
/// they are saved with the account change that needs them, through <see cref="IAccountChangeCommitter"/>.
/// </summary>
public interface IMfaRecoveryCodeRepository
{
    /// <summary>Unused codes for a user, oldest first.</summary>
    Task<IReadOnlyList<MfaRecoveryCode>> ListUnusedAsync(Guid userId, CancellationToken ct);

    Task<int> CountUnusedAsync(Guid userId, CancellationToken ct);

    /// <summary>Stages deleting every code the user holds and adding <paramref name="codes"/> in their place.</summary>
    Task StageReplacementAsync(Guid userId, IReadOnlyList<MfaRecoveryCode> codes, CancellationToken ct);

    Task StageRemovalOfAllAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Marks the code used, immediately and only if it is still unused and still exists.
    /// </summary>
    /// <returns>True when this call spent the code; false when another request spent or deleted it first.</returns>
    Task<bool> TryRedeemAsync(MfaRecoveryCode code, DateTimeOffset usedAtUtc, CancellationToken ct);
}
