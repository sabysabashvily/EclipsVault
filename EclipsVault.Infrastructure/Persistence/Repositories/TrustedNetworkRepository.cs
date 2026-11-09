using EclipsVault.Core.Application.Networks;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

/// <summary>
/// Trusted-network persistence with a short-lived cache over <see cref="ListCidrsAsync"/>, which
/// the ABAC handler calls on every access evaluation. Every mutation evicts the cache, so trusting
/// a new range takes effect on the next request rather than after the TTL.
/// </summary>
public sealed class TrustedNetworkRepository : ITrustedNetworkRepository
{
    internal const string UniqueRangeIndex = "IX_TrustedNetworks_Cidr";
    private const string CacheKey = "trusted-networks:cidrs";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly EclipsVaultDbContext _context;
    private readonly IMemoryCache _cache;

    public TrustedNetworkRepository(EclipsVaultDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<IReadOnlyList<string>> ListCidrsAsync(CancellationToken ct)
        => await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return (IReadOnlyList<string>)await _context.TrustedNetworks
                .AsNoTracking()
                .Select(t => t.Cidr)
                .ToListAsync(ct);
        }) ?? [];

    public async Task<IReadOnlyList<TrustedNetwork>> ListAsync(CancellationToken ct)
        => await _context.TrustedNetworks
            .AsNoTracking()
            .OrderBy(t => t.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(string cidr, CancellationToken ct)
        => _context.TrustedNetworks.AsNoTracking().AnyAsync(t => t.Cidr == cidr, ct);

    public async Task AddAsync(TrustedNetwork network, CancellationToken ct)
    {
        _context.TrustedNetworks.Add(network);
        await SaveAsync(ct);
        _cache.Remove(CacheKey);
    }

    public Task<TrustedNetwork?> FindAsync(Guid id, CancellationToken ct)
        => _context.TrustedNetworks.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task RemoveAsync(TrustedNetwork network, CancellationToken ct)
    {
        _context.TrustedNetworks.Remove(network);
        await SaveAsync(ct);
        _cache.Remove(CacheKey);
    }

    /// <summary>
    /// The interceptor saves the audit row in the same transaction, so any other failed save is the
    /// fail-closed audit failure. Losing the unique range to another request is a conflict instead.
    /// </summary>
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException failure) when (UniqueIndex.IsViolatedBy(failure, UniqueRangeIndex))
        {
            throw new NetworkExistsException();
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new AuditWriteFailedException("The trusted-network change and its audit row could not be stored.", failure);
        }
    }
}
