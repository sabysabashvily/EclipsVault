using System.Linq.Expressions;
using EclipsVault.Core.Domain;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence.Configurations;
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
        // The account is tracked from when it was loaded, so saving writes only the fields that
        // changed: marking it updated as a whole would write back every field as this copy last saw it.
        var account = _context.Entry(user);
        if (account.State == EntityState.Modified)
        {
            account.Property(UserConfiguration.ConcurrencyStamp).CurrentValue = Guid.NewGuid();
        }

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

    public async Task StageAvatarAsync(User user, byte[] png, CancellationToken ct)
    {
        var updatedAtUtc = _clock.GetUtcNow();
        var existing = await _context.UserAvatars.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
        if (existing is null)
        {
            _context.UserAvatars.Add(new UserAvatar { UserId = user.Id, Png = png, UpdatedAtUtc = updatedAtUtc });
        }
        else
        {
            existing.Png = png;
            existing.UpdatedAtUtc = updatedAtUtc;
        }

        user.AvatarUpdatedAtUtc = updatedAtUtc;
    }

    public async Task StageAvatarRemovalAsync(User user, CancellationToken ct)
    {
        var existing = await _context.UserAvatars.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
        if (existing is not null)
        {
            _context.UserAvatars.Remove(existing);
        }

        user.AvatarUpdatedAtUtc = null;
    }

    public async Task<bool> TryAcceptTotpStepAsync(User user, long step, CancellationToken ct)
    {
        var stamp = Guid.NewGuid();
        var accepted = await _context.Users
            .Where(u => u.Id == user.Id
                        && u.TotpEnabled
                        && u.TotpSecret == user.TotpSecret
                        && (u.LastTotpStep == null || u.LastTotpStep < step))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.LastTotpStep, step)
                .SetProperty(u => EF.Property<Guid>(u, UserConfiguration.ConcurrencyStamp), stamp), ct) == 1;
        if (accepted)
        {
            AcceptConcurrencyStamp(user, stamp);
            AcceptPersistedPropertyValue(user, u => u.LastTotpStep, step);
        }

        return accepted;
    }

    public async Task<bool> TryCompleteTotpEnrollmentAsync(User user, long step, CancellationToken ct)
    {
        var stamp = Guid.NewGuid();
        var enrolled = await _context.Users
            .Where(u => u.Id == user.Id
                        && !u.TotpEnabled
                        && u.TotpSecret == user.TotpSecret
                        && (u.LastTotpStep == null || u.LastTotpStep < step))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.TotpEnabled, true)
                .SetProperty(u => u.LastTotpStep, step)
                .SetProperty(u => EF.Property<Guid>(u, UserConfiguration.ConcurrencyStamp), stamp)
                .SetProperty(u => u.FailedAccessCount, 0)
                .SetProperty(u => u.LockedUntilUtc, (DateTimeOffset?)null), ct) == 1;
        if (enrolled)
        {
            AcceptConcurrencyStamp(user, stamp);
            AcceptPersistedPropertyValue(user, u => u.TotpEnabled, true);
            AcceptPersistedPropertyValue(user, u => u.LastTotpStep, step);
            AcceptPersistedPropertyValue(user, u => u.FailedAccessCount, 0);
            AcceptPersistedPropertyValue(user, u => u.LockedUntilUtc, null);
        }

        return enrolled;
    }

    public async Task<bool> RecordFailedAttemptAsync(User user, LockoutPolicy lockout, DateTimeOffset nowUtc, CancellationToken ct)
    {
        // Compare-and-set on the count, read again whenever another failure lands in between, so
        // concurrent failures each add one rather than all writing the same next value.
        while (true)
        {
            var failures = await _context.Users
                .Where(u => u.Id == user.Id)
                .Select(u => u.FailedAccessCount)
                .SingleAsync(ct);
            var locks = failures + 1 >= lockout.MaxFailedAttempts;
            var newCount = locks ? 0 : failures + 1;
            var lockedUntil = nowUtc + lockout.LockoutDuration;

            var counted = await _context.Users
                .Where(u => u.Id == user.Id && u.FailedAccessCount == failures)
                .ExecuteUpdateAsync(setters =>
                {
                    setters.SetProperty(u => u.FailedAccessCount, newCount);
                    if (locks)
                    {
                        setters.SetProperty(u => u.LockedUntilUtc, lockedUntil);
                    }
                }, ct) == 1;
            if (!counted)
            {
                continue;
            }

            AcceptPersistedPropertyValue(user, u => u.FailedAccessCount, newCount);
            if (locks)
            {
                AcceptPersistedPropertyValue(user, u => u.LockedUntilUtc, lockedUntil);
            }

            return locks;
        }
    }

    public async Task ClearFailedAttemptsAsync(User user, CancellationToken ct)
    {
        await _context.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.FailedAccessCount, 0)
                .SetProperty(u => u.LockedUntilUtc, (DateTimeOffset?)null), ct);
        AcceptPersistedPropertyValue(user, u => u.FailedAccessCount, 0);
        AcceptPersistedPropertyValue(user, u => u.LockedUntilUtc, null);
    }

    private void AcceptConcurrencyStamp(User user, Guid stamp)
    {
        var concurrency = _context.Entry(user).Property<Guid>(UserConfiguration.ConcurrencyStamp);
        concurrency.OriginalValue = stamp;
        concurrency.CurrentValue = stamp;
        concurrency.IsModified = false;
    }

    // ExecuteUpdate bypasses EF's tracker; accepting its value prevents the next save repeating it.
    private void AcceptPersistedPropertyValue<TValue>(User user, Expression<Func<User, TValue>> property, TValue value)
    {
        var saved = _context.Entry(user).Property(property);
        saved.OriginalValue = value;
        saved.CurrentValue = value;
        saved.IsModified = false;
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
        catch (DbUpdateConcurrencyException)
        {
            throw new AccountChangedException();
        }
        catch (DbUpdateException failure) when (ViolatesUniqueEmail(failure))
        {
            throw new EmailAddressTakenException();
        }
    }

    // SQL Server names the violated index only in its message; PostgreSQL reports it as the constraint.
    internal static bool ViolatesUniqueEmail(DbUpdateException failure) => failure.InnerException switch
    {
        SqlException sql => sql.Number is DuplicateKeyInUniqueIndex or DuplicateKeyInUniqueConstraint
                            && sql.Message.Contains(UniqueEmailIndex, StringComparison.Ordinal),
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation
                                      && postgres.ConstraintName == UniqueEmailIndex,
        _ => false,
    };
}
