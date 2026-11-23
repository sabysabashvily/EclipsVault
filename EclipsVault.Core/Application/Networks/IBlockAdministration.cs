namespace EclipsVault.Core.Application.Networks;

public interface IBlockAdministration
{
    Task<bool> LiftAsync(string network, CancellationToken ct = default);

    Task<bool> ReleaseGuardAsync(string network, CancellationToken ct = default);
}
