using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Networks;

public sealed class BlockAdministration(IIpBlacklist blacklist, IAuditSink audit) : IBlockAdministration
{
    public async Task<bool> LiftAsync(string network, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var lifted = await blacklist.UnblockAsync(network, ct);
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
                Details = "Intrusion-defence block lifted"
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
