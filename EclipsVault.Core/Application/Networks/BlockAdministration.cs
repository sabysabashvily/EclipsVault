using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Networks;

public sealed class BlockAdministration(IIpBlacklist blacklist, IAuditSink audit) : IBlockAdministration
{
    public async Task<bool> LiftAsync(string network, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await RecordAsync(await blacklist.UnblockAsync(network, ct), "Intrusion-defence block lifted");
    }

    public async Task<bool> ReleaseGuardAsync(string network, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await RecordAsync(await blacklist.ReleaseGuardAsync(network, ct), "Break-glass recovery guard released");
    }

    private async Task<bool> RecordAsync(ILiftedIpBlock? lifted, string details)
    {
        if (lifted is null)
        {
            return false;
        }

        try
        {
            // Once lifted, await the durable outcome even if the browser disconnects. A cancelled
            // audit wait can otherwise commit later, after compensation has restored the block.
            await audit.WriteAsync(new AuditEntry
            {
                Action = AuditAction.IpRangeUnblocked,
                ResourceType = "TrustedNetwork",
                ResourceName = lifted.Network,
                Details = details
            }, CancellationToken.None);
        }
        catch (Exception)
        {
            try
            {
                await lifted.RestoreAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                throw new UnblockUnrecordedException();
            }

            throw;
        }

        return true;
    }
}
