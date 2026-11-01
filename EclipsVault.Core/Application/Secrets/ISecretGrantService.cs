using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Secrets;

/// <summary>A live grant on one secret, as shown in that secret's sharing panel.</summary>
public sealed record SecretGrantDto(
    Guid Id,
    Guid GranteeUserId,
    string GranteeUsername,
    string GrantedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// A secret that has been shared with the current user, for the "Shared with me" page. It is an
/// <see cref="IGrantableResource"/> because each row is an access decision: the page lists only the
/// shares its recipient could list on the Secrets page (ADR-0027).
/// </summary>
public sealed record SharedSecretDto(
    Guid SecretId,
    string Name,
    string ProjectKey,
    SecretEnvironment Environment,
    SensitivityLevel Sensitivity,
    string GrantedBy,
    DateTimeOffset? ExpiresAtUtc) : IGrantableResource
{
    Guid IAbacResource.Id => SecretId;
}

/// <summary>A grant the current user has handed out, for the "Shared by me" access-review page.</summary>
public sealed record OutgoingShareDto(
    Guid GrantId,
    Guid SecretId,
    string SecretName,
    string GranteeUsername,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc);

/// <summary>
/// Explicit per-user access grants on secrets. Consulted by the ABAC handler (to let
/// a grant satisfy the project rule) and drives the sharing panel and "Shared with me".
/// Every grant and revocation is audited.
/// </summary>
public interface ISecretGrantService
{
    /// <summary>True when an unexpired grant exists for this user on this secret (used by ABAC).</summary>
    Task<bool> HasActiveGrantAsync(Guid userId, Guid secretId, CancellationToken ct);

    Task<IReadOnlyList<SecretGrantDto>> ListForSecretAsync(Guid secretId, CancellationToken ct);

    Task<IReadOnlyList<SharedSecretDto>> ListSharedWithUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Active shares this account has issued to others, across every secret — the "Shared by me" review.</summary>
    Task<IReadOnlyList<OutgoingShareDto>> ListIssuedByAsync(Guid issuerUserId, CancellationToken ct);

    /// <summary>Grants a user (by username or email) access to a secret. Throws SharingException on invalid input.</summary>
    Task GrantAsync(Guid secretId, string granteeUsernameOrEmail, int? ttlDays, CancellationToken ct);

    /// <summary>
    /// Revokes a grant that belongs to <paramref name="secretId"/>. The secret is a required
    /// parameter rather than context the caller is trusted to have checked: a caller is authorised
    /// against a <em>secret</em>, so revoking by grant id alone would let anyone who may share one
    /// secret revoke a grant on any other — including secrets they cannot see. Returns false when the
    /// grant is gone or belongs to a different secret, without distinguishing the two.
    /// </summary>
    Task<bool> RevokeAsync(Guid grantId, Guid secretId, CancellationToken ct);

    /// <summary>
    /// Revokes a share only if this account issued it. Returns false when the share is gone or was
    /// issued by another account — so a caller can never revoke a share that isn't theirs, and the
    /// two cases are indistinguishable to avoid leaking whether a share id exists.
    /// </summary>
    Task<bool> RevokeIssuedAsync(Guid grantId, Guid issuerUserId, CancellationToken ct);
}
