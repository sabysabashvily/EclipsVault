using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Talks to the system a dynamic credential actually lives on. One implementation per
/// <see cref="DynamicSecretBackend"/>; the service picks by <see cref="Backend"/>.
///
/// The vault generates the name and password and passes them in, so credential quality is decided
/// once in Core rather than per backend. An implementation only renders the role's statements and
/// runs them.
/// </summary>
public interface IDynamicSecretBackend
{
    DynamicSecretBackend Backend { get; }

    /// <summary>
    /// Creates the credential. Throws <see cref="Domain.Exceptions.MintRefusedException"/> when nothing
    /// reached the backend, so there is nothing to undo; any other exception means some statements may
    /// have run.
    /// </summary>
    Task MintAsync(DynamicSecretRole role, string identity, string password, DateTimeOffset expiresAtUtc, CancellationToken ct);

    /// <summary>
    /// Destroys the credential. Must be idempotent — the reaper retries, and a credential may
    /// already be gone (a DBA dropped it, or an earlier attempt half-succeeded).
    /// </summary>
    Task RevokeAsync(DynamicSecretRole role, string identity, CancellationToken ct);
}
