namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Owns the removed store record so compensation preserves its original content.
/// Restoration must fail rather than overwrite a different block raised in the meantime.
/// </summary>
public interface ILiftedIpBlock
{
    string Network { get; }

    Task RestoreAsync(CancellationToken ct = default);
}
