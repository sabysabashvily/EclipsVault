using EclipsVault.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class PasskeyCredentialRepository : IPasskeyCredentialRepository
{
    private readonly EclipsVaultDbContext _context;

    public PasskeyCredentialRepository(EclipsVaultDbContext context) => _context = context;

    public void StageAddition(PasskeyCredential credential) => _context.PasskeyCredentials.Add(credential);

    public Task<PasskeyCredential?> FindByCredentialIdAsync(byte[] credentialId, CancellationToken ct)
        => _context.PasskeyCredentials.FirstOrDefaultAsync(p => p.CredentialId == credentialId, ct);

    public async Task<bool> RefreshAsync(PasskeyCredential credential, CancellationToken ct)
    {
        var entry = _context.Entry(credential);
        await entry.ReloadAsync(ct);
        return entry.State != EntityState.Detached;
    }

    public async Task<IReadOnlyList<PasskeyCredential>> ListForUserAsync(Guid userId, CancellationToken ct)
        => await _context.PasskeyCredentials
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<PasskeyCredential?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken ct)
        => _context.PasskeyCredentials.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct);

    public async Task<bool> TryAdvanceSignCountAsync(PasskeyCredential credential, long signCount, CancellationToken ct)
    {
        var advanced = await _context.PasskeyCredentials
            .Where(key => key.Id == credential.Id && key.SignCount == credential.SignCount)
            .ExecuteUpdateAsync(setters => setters.SetProperty(key => key.SignCount, signCount), ct) == 1;
        if (advanced)
        {
            var counter = _context.Entry(credential).Property(key => key.SignCount);
            counter.OriginalValue = signCount;
            counter.CurrentValue = signCount;
            counter.IsModified = false;
        }

        return advanced;
    }

    public void StageRemoval(PasskeyCredential credential) => _context.PasskeyCredentials.Remove(credential);
}
