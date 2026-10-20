using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class SecretGrantRepository : ISecretGrantRepository
{
    private readonly EclipsVaultDbContext _context;

    public SecretGrantRepository(EclipsVaultDbContext context) => _context = context;

    public async Task AddAsync(SecretGrant grant, CancellationToken ct)
    {
        _context.SecretGrants.Add(grant);
        await _context.SaveChangesAsync(ct);
    }

    public Task<SecretGrant?> FindAsync(Guid grantId, CancellationToken ct)
        => _context.SecretGrants.FirstOrDefaultAsync(g => g.Id == grantId, ct);

    public async Task<bool> RemoveAsync(Guid grantId, CancellationToken ct)
    {
        var grant = await _context.SecretGrants.FirstOrDefaultAsync(g => g.Id == grantId, ct);
        if (grant is null)
        {
            return false;
        }

        _context.SecretGrants.Remove(grant);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> HasActiveGrantAsync(Guid userId, Guid secretId, DateTimeOffset asOfUtc, CancellationToken ct)
        => _context.SecretGrants
            .AsNoTracking()
            .AnyAsync(g => g.GranteeUserId == userId
                           && g.SecretId == secretId
                           && (g.ExpiresAtUtc == null || g.ExpiresAtUtc > asOfUtc), ct);

    public Task<bool> ExistsAsync(Guid userId, Guid secretId, CancellationToken ct)
        => _context.SecretGrants.AsNoTracking().AnyAsync(g => g.GranteeUserId == userId && g.SecretId == secretId, ct);

    public async Task<IReadOnlyList<SecretGrant>> ListForSecretAsync(Guid secretId, CancellationToken ct)
        => await _context.SecretGrants
            .AsNoTracking()
            .Where(g => g.SecretId == secretId)
            .OrderBy(g => g.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SharedSecretDto>> ListSharedWithUserAsync(Guid userId, DateTimeOffset asOfUtc, CancellationToken ct)
        => await (
            from g in _context.SecretGrants.AsNoTracking()
            join s in _context.Secrets.AsNoTracking() on g.SecretId equals s.Id
            where g.GranteeUserId == userId
                  && (g.ExpiresAtUtc == null || g.ExpiresAtUtc > asOfUtc)
                  && !s.IsShredded
                  && (s.ExpiresAtUtc == null || s.ExpiresAtUtc > asOfUtc)
            orderby s.Name
            select new SharedSecretDto(s.Id, s.Name, s.ProjectKey, s.Environment, s.Sensitivity, g.GrantedBy, g.ExpiresAtUtc))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OutgoingShareDto>> ListIssuedByAsync(Guid issuerUserId, DateTimeOffset asOfUtc, CancellationToken ct)
        => await (
            from g in _context.SecretGrants.AsNoTracking()
            join s in _context.Secrets.AsNoTracking() on g.SecretId equals s.Id
            where g.IssuerUserId == issuerUserId
                  && (g.ExpiresAtUtc == null || g.ExpiresAtUtc > asOfUtc)
                  && !s.IsShredded
            orderby g.CreatedAtUtc descending
            select new OutgoingShareDto(g.Id, s.Id, s.Name, g.GranteeUsername, g.CreatedAtUtc, g.ExpiresAtUtc))
            .ToListAsync(ct);
}
