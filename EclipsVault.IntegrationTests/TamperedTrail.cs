using EclipsVault.Core.Application.Auditing;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The audit trail as someone with database access left it, put back exactly on dispose. Rows the vault
/// writes in the meantime are chained onto the tampered trail, so they are deleted before the restore.
/// </summary>
internal sealed class TamperedTrail : IAsyncDisposable
{
    private readonly VaultFixture _vault;
    private readonly long _tamperedHead;
    private readonly Func<EclipsVaultDbContext, Task> _restore;

    private TamperedTrail(VaultFixture vault, long tamperedHead, Func<EclipsVaultDbContext, Task> restore)
    {
        _vault = vault;
        _tamperedHead = tamperedHead;
        _restore = restore;
    }

    /// <summary>
    /// Deletes every entry from <paramref name="sequence"/> to the head. Verify reads only chained rows,
    /// those above sequence zero, so a row moved below zero is deleted as far as it can tell, and can come back intact.
    /// </summary>
    public static async Task<TamperedTrail> DeleteFromAsync(VaultFixture vault, long sequence)
    {
        using (var scope = vault.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AuditLogs
                .Where(row => row.Sequence >= sequence)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Sequence, row => -row.Sequence));
        }

        return new TamperedTrail(vault, sequence - 1, db => db.AuditLogs
            .Where(row => row.Sequence <= -sequence)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Sequence, row => -row.Sequence)));
    }

    /// <summary>Rewrites one entry's details and recomputes every hash from it to the head, so the chain still links.</summary>
    public static async Task<TamperedTrail> RewriteFromAsync(VaultFixture vault, long sequence, string details)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var previousHash = await db.AuditLogs.Where(row => row.Sequence == sequence - 1).Select(row => row.EntryHash!).SingleAsync();
        var rows = await db.AuditLogs.AsNoTracking().Where(row => row.Sequence >= sequence).OrderBy(row => row.Sequence).ToListAsync();
        var originals = rows.Select(row => (row.Sequence, row.Details, row.PreviousHash, row.EntryHash)).ToArray();

        rows[0].Details = details;
        foreach (var row in rows)
        {
            row.PreviousHash = previousHash;
            row.EntryHash = AuditRowHasher.Compute(row, previousHash);
            previousHash = row.EntryHash;
            await SetAsync(db, row.Sequence, row.Details, row.PreviousHash, row.EntryHash);
        }

        return new TamperedTrail(vault, rows[^1].Sequence, async restoring =>
        {
            foreach (var original in originals)
            {
                await SetAsync(restoring, original.Sequence, original.Details, original.PreviousHash, original.EntryHash);
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        using var scope = _vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.AuditLogs.Where(row => row.Sequence > _tamperedHead).ExecuteDeleteAsync();
        await _restore(db);
    }

    private static Task SetAsync(EclipsVaultDbContext db, long sequence, string? details, string? previousHash, string? entryHash)
        => db.AuditLogs.Where(row => row.Sequence == sequence).ExecuteUpdateAsync(set => set
            .SetProperty(row => row.Details, details)
            .SetProperty(row => row.PreviousHash, previousHash)
            .SetProperty(row => row.EntryHash, entryHash));
}
