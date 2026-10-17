using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Users;

/// <summary>
/// Administrative user lifecycle. New accounts receive an Argon2id hash with a fresh
/// random salt, an auto-generated unique email, and enroll TOTP at their first
/// sign-in; role changes, disabling, and deletions are guarded against locking the
/// vault out of its own administration.
/// </summary>
public sealed class UserAdminService : IUserAdminService
{
    private const int MinimumPasswordLength = 12;

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ISessionRevocationService _revocation;
    private readonly IMfaRecoveryCodeRepository _recoveryCodes;
    private readonly IBreachedPasswordScreen _breachScreen;
    private readonly UserDirectoryOptions _directory;
    private readonly INotificationService _notifications;
    private readonly IAuditSink _audit;
    private readonly IAuditContext _actor;
    private readonly TimeProvider _clock;

    public UserAdminService(
        IUserRepository users,
        IPasswordHasher hasher,
        ISessionRevocationService revocation,
        IMfaRecoveryCodeRepository recoveryCodes,
        IBreachedPasswordScreen breachScreen,
        UserDirectoryOptions directory,
        INotificationService notifications,
        IAuditSink audit,
        IAuditContext actor,
        TimeProvider clock)
    {
        _users = users;
        _hasher = hasher;
        _revocation = revocation;
        _recoveryCodes = recoveryCodes;
        _breachScreen = breachScreen;
        _directory = directory;
        _notifications = notifications;
        _audit = audit;
        _actor = actor;
        _clock = clock;
    }

    private Task AuditUserAsync(AuditAction action, Guid? userId, string username, string? details, CancellationToken ct)
        => _audit.WriteUserEventAsync(action, userId, username, details, ct);

    public async Task<IReadOnlyList<UserSummaryDto>> ListAsync(CancellationToken ct)
    {
        var users = await _users.ListAsync(ct);
        return users.Select(Map).ToList();
    }

    public async Task<UserSummaryDto?> GetAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        return user is null ? null : Map(user);
    }

    public async Task<CreatedUserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        if (username.Length < 3)
        {
            throw new VaultAdminException("Usernames must be at least 3 characters long.");
        }

        var first = request.FirstName.Trim();
        var last = request.LastName.Trim();
        if (first.Length == 0 || last.Length == 0)
        {
            throw new VaultAdminException("First name and last name are required.");
        }

        if (request.Password.Length < MinimumPasswordLength)
        {
            throw new VaultAdminException($"Passwords must be at least {MinimumPasswordLength} characters long.");
        }

        if (_breachScreen.IsCompromised(request.Password))
        {
            throw new VaultAdminException("That password has appeared in a known data breach. Choose a different one.");
        }

        if (await _users.FindByUsernameAsync(username, ct) is not null)
        {
            throw new VaultAdminException($"The username '{username}' is already taken.");
        }

        var email = await GenerateEmailAsync(first, last, ct);
        var displayName = $"{first} {last}";

        var hashed = _hasher.Hash(request.Password);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = displayName,
            Email = email,
            PasswordHash = hashed.Hash,
            PasswordSalt = hashed.Salt,
            Clearance = request.Clearance,
            ProjectKey = request.ProjectKey.Trim().ToUpperInvariant(),
            CreatedAtUtc = _clock.GetUtcNow()
        };

        await _users.AddAsync(user, ct);
        await AuditUserAsync(
            AuditAction.UserCreated, user.Id, _actor.Username ?? "system",
            $"Created '{user.Username}' <{email}> (clearance {user.Clearance}, project {user.ProjectKey})", ct);
        await _notifications.NotifyUserProvisionedAsync(email, displayName, user.Username, ct);

        return new CreatedUserDto(user.Id, user.Username, email, displayName);
    }

    /// <summary>
    /// Builds a unique <c>first.last.N@domain</c> email. N is the next free sequence
    /// for that name, so "Saba Sabashvili" yields saba.sabashvili.1@…, then …2@… .
    /// </summary>
    private async Task<string> GenerateEmailAsync(string first, string last, CancellationToken ct)
    {
        var prefix = $"{Slug(first)}.{Slug(last)}";
        var existing = await _users.FindEmailsWithPrefixAsync(prefix, _directory.EmailDomain, ct);
        var next = existing.Select(e => ParseSequence(e, prefix)).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}.{next}@{_directory.EmailDomain}";
    }

    private static int ParseSequence(string email, string prefix)
    {
        var at = email.IndexOf('@');
        if (at < 0)
        {
            return 0;
        }

        var local = email[..at]; // expected: prefix.N
        if (!local.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return int.TryParse(local[(prefix.Length + 1)..], out var n) ? n : 0;
    }

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? "user" : new string(chars);
    }

    public async Task<bool> SetRoleAsync(Guid userId, ClearanceLevel clearance, string projectKey, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        // Refuse to remove the last administrator's clearance.
        if (user.Clearance == ClearanceLevel.TopSecret && clearance != ClearanceLevel.TopSecret
            && await IsLastAdminAsync(ct))
        {
            throw new VaultAdminException("Cannot demote the last TopSecret administrator — the vault would become unmanageable.");
        }

        var previous = $"{user.Clearance}/{user.ProjectKey}";
        user.Clearance = clearance;
        user.ProjectKey = projectKey.Trim().ToUpperInvariant();
        await _users.UpdateAsync(user, ct);

        // A clearance/project change alters what the user may reach, so drop their
        // current sessions; the new attributes take effect at their next sign-in.
        await _revocation.RevokeAsync(user.Id, _clock.GetUtcNow(), ct);

        await AuditUserAsync(
            AuditAction.UserRoleChanged, user.Id, _actor.Username ?? "system",
            $"Role for '{user.Username}' changed {previous} → {user.Clearance}/{user.ProjectKey}; sessions revoked", ct);
        return true;
    }

    public async Task<bool> SetEnabledAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        if (!enabled && _actor.UserId == userId)
        {
            throw new VaultAdminException("You cannot disable your own account while signed in with it.");
        }

        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        if (!enabled && user.Clearance == ClearanceLevel.TopSecret && await IsLastAdminAsync(ct))
        {
            throw new VaultAdminException("Cannot disable the last TopSecret administrator.");
        }

        if (user.IsDisabled == !enabled)
        {
            return true; // already in the requested state
        }

        user.IsDisabled = !enabled;
        await _users.UpdateAsync(user, ct);

        if (!enabled)
        {
            // Kill active sessions immediately; new sign-ins are blocked at credential check.
            await _revocation.RevokeAsync(user.Id, _clock.GetUtcNow(), ct);
        }

        await AuditUserAsync(
            enabled ? AuditAction.UserEnabled : AuditAction.UserDisabled,
            user.Id, _actor.Username ?? "system",
            $"Account '{user.Username}' {(enabled ? "enabled" : "disabled")}", ct);
        return true;
    }

    public async Task<bool> ForceLogoutAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        await _revocation.RevokeAsync(user.Id, _clock.GetUtcNow(), ct);
        await AuditUserAsync(
            AuditAction.UserForceLoggedOut, user.Id, _actor.Username ?? "system",
            $"All sessions for '{user.Username}' revoked by administrator", ct);
        return true;
    }

    public async Task<bool> ResetTotpAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        user.TotpSecret = null;
        user.TotpEnabled = false;
        await _users.UpdateAsync(user, ct);
        // Old recovery codes belong to the retired authenticator; clear them so they cannot bypass the fresh enrollment.
        await _recoveryCodes.DeleteAllAsync(user.Id, ct);
        await AuditUserAsync(
            AuditAction.UserTotpReset, user.Id, _actor.Username ?? "system",
            $"MFA reset for '{user.Username}'; TOTP re-enrollment required at next sign-in", ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid userId, CancellationToken ct)
    {
        if (_actor.UserId == userId)
        {
            throw new VaultAdminException("You cannot delete your own account while signed in with it.");
        }

        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        if (user.Clearance == ClearanceLevel.TopSecret && await IsLastAdminAsync(ct))
        {
            throw new VaultAdminException("Cannot delete the last TopSecret administrator — the vault would become unmanageable.");
        }

        // A session carries the account's clearance and project, so one that outlived the account
        // would keep its access. Revoked first: if the delete then fails, the account only lost its sessions.
        await _revocation.RevokeAsync(user.Id, _clock.GetUtcNow(), ct);
        await _users.DeleteAsync(user, ct);
        await AuditUserAsync(
            AuditAction.UserDeleted, userId, _actor.Username ?? "system",
            $"Deleted '{user.Username}'; sessions revoked", ct);
        return true;
    }

    public async Task<bool> UnlockAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        if (user.FailedAccessCount == 0 && user.LockedUntilUtc is null)
        {
            return true; // nothing to clear
        }

        user.FailedAccessCount = 0;
        user.LockedUntilUtc = null;
        await _users.UpdateAsync(user, ct);
        await AuditUserAsync(
            AuditAction.AccountUnlocked, user.Id, _actor.Username ?? "system",
            $"Lockout cleared for '{user.Username}'", ct);
        return true;
    }

    private async Task<bool> IsLastAdminAsync(CancellationToken ct)
    {
        var all = await _users.ListAsync(ct);
        return all.Count(u => u.Clearance == ClearanceLevel.TopSecret) <= 1;
    }

    private UserSummaryDto Map(User u) =>
        new(u.Id, u.Username, u.DisplayName, u.Email, u.Clearance, u.ProjectKey,
            u.TotpEnabled, u.IsDisabled, u.IsLockedOut(_clock.GetUtcNow()), u.AvatarUpdatedAtUtc is not null, u.CreatedAtUtc);
}
