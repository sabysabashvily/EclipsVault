namespace EclipsVault.Core.Application.Sessions;

/// <summary>
/// An account's signed-in devices: the registry's sessions issued under the account's current
/// credential generation and revocation version. A credential change or signing out everywhere
/// takes the earlier sessions off the list at once, without waiting for their records to expire.
/// </summary>
public interface ISignedInDevices
{
    /// <summary>The account's live sessions, most-recently-active first; none when the account no longer exists.</summary>
    Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, CancellationToken ct);
}
