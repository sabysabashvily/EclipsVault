
namespace EclipsVault.Core.Application.Secrets;

/// <summary>Application-layer facade for the secret lifecycle. Controllers depend on this, never on persistence.</summary>
public interface ISecretService
{
    Task<IReadOnlyList<SecretSummaryDto>> ListAsync(CancellationToken ct);

    /// <summary>Looks a secret up by id for an access check. Trips the honey-token trap; records nothing.</summary>
    Task<SecretDetailsDto> GetDetailsAsync(Guid id, CancellationToken ct);

    /// <summary>Records a served details page. Fail-closed: the page is not served if this throws.</summary>
    Task RecordViewedAsync(SecretDetailsDto secret, CancellationToken ct);

    /// <summary>
    /// Records a by-id action the caller was refused, with the policy's reasons. Fail-closed: the
    /// refusal is not answered with its reasons if this throws.
    /// </summary>
    Task RecordAccessDeniedAsync(SecretDetailsDto secret, IEnumerable<string> reasons, CancellationToken ct);

    /// <summary>Decrypts the payload. Audit row is committed BEFORE any decryption happens (fail-closed).</summary>
    Task<RevealedSecretDto> RevealAsync(Guid id, CancellationToken ct);

    Task<Guid> CreateAsync(CreateSecretRequest request, CancellationToken ct);

    /// <summary>
    /// Replaces the value, archiving the current one as a version. <paramref name="renewTtlDays"/>
    /// resets the expiry to that many days from now — the renewal the "Expiring soon" panel points
    /// at; null leaves the existing deadline untouched.
    /// </summary>
    Task RotateAsync(Guid id, string newValue, string? changeNote, int? renewTtlDays, CancellationToken ct);

    /// <summary>
    /// Rotates a <b>managed</b> secret: the vault picks a new password, changes the real principal
    /// on its backend, and stores the result — so the stored value and the live credential move
    /// together instead of an operator changing one and pasting the other. Throws
    /// <c>VaultAdminException</c> if the secret is not bound to a backend principal,
    /// <c>UpstreamRotationRefusedException</c> if the backend refuses, and
    /// <c>UpstreamRotationDriftedException</c> if the login and the stored value may now disagree.
    /// </summary>
    Task RotateManagedAsync(Guid id, int? renewTtlDays, CancellationToken ct);

    /// <summary>Lists the archived (superseded) values of a secret, newest first. Metadata only.</summary>
    Task<IReadOnlyList<SecretVersionDto>> ListVersionsAsync(Guid id, CancellationToken ct);

    /// <summary>Decrypts an archived version. Audit row is committed BEFORE any decryption (fail-closed).</summary>
    Task<RevealedSecretDto> RevealVersionAsync(Guid id, Guid versionId, CancellationToken ct);

    /// <summary>Reverts the secret's live value to an archived version, archiving the current value first, recorded as a restore.</summary>
    Task RestoreVersionAsync(Guid id, Guid versionId, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);
}
