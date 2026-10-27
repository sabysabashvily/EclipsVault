namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Holds each account's session revocation version in the shared session store (Redis, or process
/// memory on a single node). A session is valid only while the version it was issued under is still
/// the account's current one, so signing out everywhere replaces the version. Versions are random
/// rather than timestamps: two changes in the same second stay distinct, and a version never comes
/// back, even after the store loses its state.
/// </summary>
public interface ISessionRevocationService
{
    /// <summary>The account's current version, first creating a new one if the store holds none.</summary>
    Task<string> CaptureAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The account's current version, or null when the store holds none. Then no existing session of
    /// the account is valid: the version it carries may have been revoked in the state the store lost.
    /// </summary>
    Task<string?> FindAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Replaces the account's version, so every session issued under an earlier one is refused.</summary>
    Task RevokeAsync(Guid userId, CancellationToken ct = default);
}
