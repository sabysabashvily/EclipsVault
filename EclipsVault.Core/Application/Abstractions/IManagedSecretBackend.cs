using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Changes the password of a principal that already exists on a backend — the other half of
/// <see cref="IDynamicSecretBackend"/>, which creates and destroys its own.
///
/// This is what lets the vault rotate the <i>real</i> upstream credential rather than only
/// re-encrypting a value someone changed by hand elsewhere. Without it, "rotate" means an operator
/// changes the password upstream, then pastes it in — two steps that can silently disagree, leaving
/// the vault confidently serving a password that no longer works.
///
/// Unlike a dynamic role, there are no operator-supplied statements here: the backend knows how to
/// re-password its own principal type, so no per-secret SQL is stored or run.
/// </summary>
public interface IManagedSecretBackend
{
    DynamicSecretBackend Backend { get; }

    /// <summary>
    /// True when <paramref name="password"/> can be sent through <see cref="RotatePrincipalAsync"/>.
    /// Checked under the principal lock against the currently stored value before the first upstream write
    /// so compensation cannot fail to restore the previous password if a later write fails.
    /// </summary>
    bool CanApplyPassword(string password) => true;

    /// <summary>
    /// Sets <paramref name="principal"/>'s password to <paramref name="newPassword"/>. Throws
    /// <c>UpstreamRotationRefusedException</c> only when the password certainly did not change; after
    /// any other failure the caller must assume it may have.
    /// </summary>
    Task RotatePrincipalAsync(string principal, string newPassword, CancellationToken ct);
}
