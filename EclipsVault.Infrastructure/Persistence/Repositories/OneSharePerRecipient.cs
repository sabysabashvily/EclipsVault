using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

internal static class OneSharePerRecipient
{
    public const string Index = "IX_SecretGrants_SecretId_GranteeUserId";

    public static async Task<bool> AnotherShareExistsAsync(EclipsVaultDbContext context, SecretGrant share, Guid? replacedShareId, CancellationToken ct)
    {
        try
        {
            return await context.SecretGrants.AsNoTracking().AnyAsync(other => other.SecretId == share.SecretId
                && other.GranteeUserId == share.GranteeUserId
                && other.Id != replacedShareId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database that failed the save may still be unreachable. Nothing shows a race was
            // lost, so the save's own failure is the answer.
            return false;
        }
    }
}
