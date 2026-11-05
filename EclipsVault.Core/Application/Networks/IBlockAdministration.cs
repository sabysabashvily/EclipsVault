namespace EclipsVault.Core.Application.Networks;

public interface IBlockAdministration
{
    Task<bool> LiftAsync(string network, CancellationToken ct = default);
}
