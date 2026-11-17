using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.DynamicSecrets;

/// <summary>
/// Issues and reclaims short-lived backend credentials.
///
/// The vault stores no value for these: it mints one on request, hands it over once, and destroys
/// it when the lease ends. ABAC gating is the caller's job (the handler runs on the role, which is
/// an <see cref="Abac.IAbacResource"/>) — this service owns the lease lifecycle.
/// </summary>
public interface IDynamicSecretService
{
    Task<IReadOnlyList<DynamicSecretRoleDto>> ListRolesAsync(CancellationToken ct);

    Task<DynamicSecretRoleDto?> FindRoleAsync(Guid roleId, CancellationToken ct);

    /// <summary>
    /// Mints a credential and opens a lease. <paramref name="ttlMinutes"/>, or the role's default when
    /// null, is clamped to the role's ceiling and to a day. The returned secret is never retrievable again.
    /// </summary>
    /// <exception cref="Domain.Exceptions.MintRefusedException">The mint failed and its login was removed.</exception>
    /// <exception cref="Domain.Exceptions.MintIncompleteException">The mint failed and its login may remain.</exception>
    /// <exception cref="Domain.Exceptions.MintUnrecordedException">The lease was not recorded and its login may remain.</exception>
    Task<IssuedCredentialDto> IssueAsync(Guid roleId, int? ttlMinutes, CancellationToken ct);

    /// <summary>Records that the caller was refused a credential for <paramref name="role"/>. Fail-closed.</summary>
    Task RecordAccessDeniedAsync(DynamicSecretRoleDto role, IEnumerable<string> reasons, CancellationToken ct);

    Task<IReadOnlyList<LeaseDto>> ListLeasesAsync(Guid userId, bool includeEveryone, CancellationToken ct);

    /// <summary>
    /// Hands a credential back early. A caller may only revoke their own lease unless
    /// <paramref name="isAdmin"/>. Returns how the lease closed: <see cref="Domain.Enums.LeaseStatus.Revoked"/>
    /// when the backend destroyed the credential, <see cref="Domain.Enums.LeaseStatus.RevocationFailed"/>
    /// when it did not. Returns null when the lease is unknown, already closed, or someone else's —
    /// indistinguishable on purpose, so this cannot be used to probe for leases.
    /// </summary>
    /// <exception cref="Domain.Exceptions.RevokedUnrecordedException">The credential was destroyed but the close was not recorded.</exception>
    Task<LeaseStatus?> RevokeAsync(Guid leaseId, Guid userId, bool isAdmin, CancellationToken ct);

    /// <summary>Destroys every credential whose lease has elapsed. Returns how many were closed.</summary>
    Task<int> ReapDueLeasesAsync(CancellationToken ct);

    /// <summary>The role behind a lease, for re-checking access before revoking.</summary>
    Task<DynamicSecretLease?> FindLeaseAsync(Guid leaseId, CancellationToken ct);
}
