using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

internal static class AuditTrail
{
    public static async Task<long> HeadAsync(VaultFixture vault)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.AuditLogs.MaxAsync(row => row.Sequence);
    }

    public static async Task<AuditLog[]> ShareRowsAfterAsync(VaultFixture vault, long sequence)
        => (await RowsAfterAsync(vault, sequence))
            .Where(row => row.Action is AuditAction.SecretShared or AuditAction.SecretShareRevoked)
            .ToArray();

    public static async Task<AuditLog[]> RowsAfterAsync(VaultFixture vault, long sequence)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.AuditLogs.AsNoTracking().Where(row => row.Sequence > sequence).OrderBy(row => row.Sequence).ToArrayAsync();
    }
}
