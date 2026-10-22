using EclipsVault.Core.Domain;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EclipsVault.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private const string UniqueEmailIndex = "IX_Users_NormalizedEmail";
    private const int DuplicateKeyInUniqueIndex = 2601;
    private const int DuplicateKeyInUniqueConstraint = 2627;

    private readonly EclipsVaultDbContext _context;
    private readonly TimeProvider _clock;

    public UserRepository(EclipsVaultDbContext context, TimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public Task<User?> FindByUsernameAsync(string username, CancellationToken ct)
        => _context.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

    public async Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct)
    {
        var email = EmailIdentity.Normalize(identifier);
        var matches = await _context.Users
            .Where(u => u.Username == identifier || u.NormalizedEmail == email)
            .Take(2)
            .ToListAsync(ct);
        return matches.Count == 1 ? matches[0] : null;
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var normalized = EmailIdentity.Normalize(email);
        return _context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
    }

    public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct)
    {
        // A username takes the address as the migration's preflight decides: it is the address as
        // the username column compares it, or the same email identity.
        var normalizedUsername = _context.Database.IsNpgsql()
            ? EmailIdentitySql.NormalizePostgres("\"Username\"")
            : EmailIdentitySql.NormalizeSqlServer("\"Username\"");
        var taken = $$"""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM "Users"
                WHERE "Id" <> {0} AND ("NormalizedEmail" = {1} OR "Username" = {2} OR {{normalizedUsername}} = {1})
            ) THEN 1 ELSE 0 END AS "Value"
            """;
        return _context.Database
            .SqlQueryRaw<int>(taken, accountId, EmailIdentity.Normalize(email), email)
            .Select(found => found == 1)
            .SingleAsync(ct);
    }

    public async Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct)
    {
        var pattern = EmailIdentity.Normalize($"{localPrefix}.%@{domain}");
        return await _context.Users
            .AsNoTracking()
            .Where(u => EF.Functions.Like(u.NormalizedEmail, pattern))
            .Select(u => u.NormalizedEmail)
            .ToListAsync(ct);
    }

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct)
        => _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct)
        => await _context.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct)
    {
        _context.Users.Add(user);
        await SaveClaimingEmailAsync(ct);
    }

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        _context.Users.Update(user);
        await SaveClaimingEmailAsync(ct);
    }

    public async Task DeleteAsync(User user, CancellationToken ct)
    {
        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);
    }

    public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct)
        => _context.UserAvatars
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.Png)
            .FirstOrDefaultAsync(ct)!;

    public async Task SetAvatarAsync(User user, byte[] png, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var existing = await _context.UserAvatars.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
        if (existing is null)
        {
            _context.UserAvatars.Add(new UserAvatar { UserId = user.Id, Png = png, UpdatedAtUtc = now });
        }
        else
        {
            existing.Png = png;
            existing.UpdatedAtUtc = now;
        }

        user.AvatarUpdatedAtUtc = now;
        _context.Users.Update(user);
        await _context.SaveChangesAsync(ct);
    }

    public async Task RemoveAvatarAsync(User user, CancellationToken ct)
    {
        var existing = await _context.UserAvatars.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
        if (existing is not null)
        {
            _context.UserAvatars.Remove(existing);
        }

        user.AvatarUpdatedAtUtc = null;
        _context.Users.Update(user);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Saves, reporting a lost race for an email address as the conflict an earlier check would
    /// have reported, not as a database fault.
    /// </summary>
    private async Task SaveClaimingEmailAsync(CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException failure) when (ViolatesUniqueEmail(failure))
        {
            throw new EmailAddressTakenException();
        }
    }

    // SQL Server names the violated index only in its message; PostgreSQL reports it as the constraint.
    private static bool ViolatesUniqueEmail(DbUpdateException failure) => failure.InnerException switch
    {
        SqlException sql => sql.Number is DuplicateKeyInUniqueIndex or DuplicateKeyInUniqueConstraint
                            && sql.Message.Contains(UniqueEmailIndex, StringComparison.Ordinal),
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation
                                      && postgres.ConstraintName == UniqueEmailIndex,
        _ => false,
    };
}
