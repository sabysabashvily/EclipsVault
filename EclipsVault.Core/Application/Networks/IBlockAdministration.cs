namespace EclipsVault.Core.Application.Networks;

public interface IBlockAdministration
{
    Task<bool> LiftAsync(string network, CancellationToken ct = default);

    /// <summary>Releases the stuck break-glass guards on <paramref name="network"/>; false when there are none.</summary>
    Task<bool> ReleaseGuardAsync(string network, CancellationToken ct = default);
}
