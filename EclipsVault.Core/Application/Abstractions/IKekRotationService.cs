using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Key lifecycle management. Rotation re-wraps every secret's (and archived version's) data-encryption
/// key under the <em>current</em> master KEK and retires the old one — the payload ciphertext never
/// changes. The operator supplies the new current KEK and keeps the previous key available as a retired
/// key (so existing DEKs can still be unwrapped) until rotation completes; afterwards the old key can be
/// removed. Aligns with NIST 800-57 cryptoperiods and PCI-DSS 3.6/3.7.
/// </summary>
public interface IKekRotationService
{
    Task<KekStatus> GetStatusAsync(CancellationToken ct);

    /// <summary>
    /// Runs one pass in batches (ADR-0029). Throws <see cref="KekRotationRunningException"/>, having changed
    /// nothing, while another pass runs, and <see cref="KekRotationStoppedException"/> when a batch cannot be
    /// recorded; the batches committed before it stay, and the exception counts what they moved.
    /// </summary>
    Task<KekRotationResult> RotateAsync(CancellationToken ct);
}
