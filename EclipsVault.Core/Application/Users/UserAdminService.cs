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
    private const int MaximumPasswordLength = 256;
    private const int MaximumProjectKeyLength = 64;
    private const int MaximumNameLength = 64;

    /// <summary>The display name column's width, which "First Last" must fit.</summary>
    private const int MaximumDisplayNameLength = 64;

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ISessionRevocationService _revocation;
    private readonly IMfaRecoveryCodeRepository _recoveryCodes;
    private readonly IBreachedPasswordScreen _breachScreen;
    private readonly UserDirectoryOptions _directory;
    private readonly INotificationService _notifications;
    private readonly IAuditSink _audit;
    private readonly IAccountChangeCommitter _changes;
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
        IAccountChangeCommitter changes,
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
        _changes = changes;
        _actor = actor;
        _clock = clock;
    }

    /// <summary>An administrator's action on an account, attributed to the administrator.</summary>
    private AuditEntry AdministratorEvent(AuditAction action, User account, string details) => new()
    {
        Action = action,
        ResourceType = nameof(User),
        ResourceId = account.Id,
        ActorUserId = _actor.UserId,
        ActorUsername = _actor.Username ?? "system",
        Details = details
    };

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
        var first = request.FirstName.Trim();
        var last = request.LastName.Trim();
        var displayName = $"{first} {last}";
        CheckNewAccount(request, username, first, last, displayName);
        CheckClearance(request.Clearance);
        var project = CheckedProjectKey(request.ProjectKey);
        if (_breachScreen.IsCompromised(request.Password))
        {
            throw new AccountInputException(nameof(CreateUserRequest.Password), "That password has appeared in a known data breach. Choose a different one.");
        }

        if (await _users.FindByUsernameAsync(username, ct) is not null)
        {
            throw UsernameTaken(username);
        }

        var email = await GenerateEmailAsync(first, last, ct);
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
            ProjectKey = project,
            CreatedAtUtc = _clock.GetUtcNow()
        };

        // Another create for the same person's name can take the generated address first. The account
        // then takes the next free one, once; losing twice in a row means the name is busy right now.
        if (!await TryCommitNewAccountAsync(user, ct))
        {
            user.Email = await GenerateEmailAsync(first, last, ct);
            if (!await TryCommitNewAccountAsync(user, ct))
            {
                throw new VaultAdminException("Another account for the same name was created at the same moment. Try again.");
            }
        }

        await _notifications.NotifyUserProvisionedAsync(user.Email, displayName, user.Username, ct);
        return new CreatedUserDto(user.Id, user.Username, user.Email, displayName);
    }

    /// <summary>Saves the new account with its UserCreated event.</summary>
    /// <returns>False when another account took its email address first; nothing was saved.</returns>
    private async Task<bool> TryCommitNewAccountAsync(User user, CancellationToken ct)
    {
        _users.StageAddition(user);
        try
        {
            await _changes.CommitAsync(user, AdministratorEvent(AuditAction.UserCreated, user,
                $"Created '{user.Username}' <{user.Email}> (clearance {user.Clearance}, project {user.ProjectKey})"), ct);
            return true;
        }
        catch (UsernameTakenException)
        {
            throw UsernameTaken(user.Username);
        }
        catch (EmailAddressTakenException)
        {
            // The same username created twice at once also gets the same generated address, and the
            // database may report that index first. The username is what the administrator chose, so name it.
            if (await _users.FindByUsernameAsync(user.Username, ct) is not null)
            {
                throw UsernameTaken(user.Username);
            }

            return false;
        }
    }

    private static VaultAdminException UsernameTaken(string username) => new($"The username '{username}' is already taken.");

    /// <summary>
    /// The New user form's rules for the username, names and password, so callers other than the form are
    /// held to them too. Clearance and project key have checks of their own, shared with role changes.
    /// </summary>
    private static void CheckNewAccount(CreateUserRequest request, string username, string first, string last, string displayName)
    {
        if (!UsernameRules.Allows(username))
        {
            throw new AccountInputException(nameof(CreateUserRequest.Username),
                $"Usernames are {UsernameRules.MinimumLength} to {UsernameRules.MaximumLength} characters. {UsernameRules.Description}");
        }

        CheckName(nameof(CreateUserRequest.FirstName), "First name", first);
        CheckName(nameof(CreateUserRequest.LastName), "Last name", last);
        if (displayName.Length > MaximumDisplayNameLength)
        {
            throw new AccountInputException(nameof(CreateUserRequest.LastName),
                $"First and last name together can be at most {MaximumDisplayNameLength} characters, counting the space between them.");
        }

        if (request.Password.Length < MinimumPasswordLength)
        {
            throw new AccountInputException(nameof(CreateUserRequest.Password), $"Passwords must be at least {MinimumPasswordLength} characters long.");
        }

        if (request.Password.Length > MaximumPasswordLength)
        {
            throw new AccountInputException(nameof(CreateUserRequest.Password), $"Passwords can be at most {MaximumPasswordLength} characters long.");
        }
    }

    private static void CheckName(string field, string label, string name)
    {
        if (name.Length == 0)
        {
            throw new AccountInputException(field, $"{label} is required.");
        }

        if (name.Length > MaximumNameLength)
        {
            throw new AccountInputException(field, $"{label} can be at most {MaximumNameLength} characters.");
        }
    }

    /// <summary>Any integer binds to the enum, and an undefined clearance passes sensitivity checks without being an administrator.</summary>
    private static void CheckClearance(ClearanceLevel clearance)
    {
        if (!Enum.IsDefined(clearance))
        {
            throw new AccountInputException(nameof(CreateUserRequest.Clearance), "Choose one of the vault's clearance levels.");
        }
    }

    /// <returns>The project key as it is stored: trimmed and upper case.</returns>
    private static string CheckedProjectKey(string projectKey)
    {
        var project = projectKey.Trim();
        if (project.Length is 0 or > MaximumProjectKeyLength)
        {
            throw new AccountInputException(nameof(CreateUserRequest.ProjectKey),
                $"Project keys are 1 to {MaximumProjectKeyLength} characters, not counting spaces at either end.");
        }

        return project.ToUpperInvariant();
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
        // The scan above sees only emails. An older account's username can also be this address,
        // and the new account is not saved yet, so no account's own address is exempt.
        var notYetSaved = Guid.Empty;
        while (await _users.IsEmailTakenAsync(Address(next), notYetSaved, ct))
        {
            next++;
        }

        return Address(next);

        string Address(int sequence) => $"{prefix}.{sequence}@{_directory.EmailDomain}";
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
        CheckClearance(clearance);
        var project = CheckedProjectKey(projectKey);
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        if (user.Clearance == clearance && user.ProjectKey == project)
        {
            return true;
        }

        var demotesAnAdministrator = IsEnabledAdministrator(user) && clearance != ClearanceLevel.TopSecret;
        var previous = $"{user.Clearance}/{user.ProjectKey}";
        user.Clearance = clearance;
        user.ProjectKey = project;
        // A session carries the clearance and project it was signed in with, so every earlier session
        // ends with the change: the new role takes effect at the account's next sign-in.
        user.CredentialGeneration++;
        await CommitKeepingAnAdministratorAsync(user, demotesAnAdministrator, AdministratorEvent(AuditAction.UserRoleChanged, user,
            $"Role for '{user.Username}' changed {previous} → {user.Clearance}/{user.ProjectKey}; sessions revoked"),
            "Cannot demote the last TopSecret administrator — the vault would become unmanageable.", ct);
        return true;
    }

    public async Task<bool> SetEnabledAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        if (!enabled && _actor.UserId == userId)
        {
            throw new OwnAccountException("You cannot disable your own account while signed in with it.");
        }

        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        if (user.IsDisabled == !enabled)
        {
            return true; // already in the requested state
        }

        var disablesAnAdministrator = !enabled && IsEnabledAdministrator(user);
        user.IsDisabled = !enabled;
        // Both directions advance the generation (ADR-0023). Disabling ends every session in this commit;
        // enabling also ends any that an earlier disable ended only in the session store, if the store lost it.
        user.CredentialGeneration++;

        await CommitKeepingAnAdministratorAsync(user, disablesAnAdministrator,
            AdministratorEvent(enabled ? AuditAction.UserEnabled : AuditAction.UserDisabled, user, $"Account '{user.Username}' {(enabled ? "enabled" : "disabled")}"),
            "Cannot disable the last TopSecret administrator.", ct);
        return true;
    }

    public async Task<bool> ForceLogoutAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        // Sessions live in the session store, outside the database's transaction. Revoking first means
        // an audit failure never leaves them open (ADR-0021).
        await _revocation.RevokeAsync(user.Id, ct);
        await _audit.WriteAsync(AdministratorEvent(AuditAction.UserForceLoggedOut, user,
            $"All sessions for '{user.Username}' revoked by administrator"), ct);
        return true;
    }

    public async Task<bool> ResetTotpAsync(Guid userId, CancellationToken ct)
    {
        // Profile resets your own authenticator after confirming it is you (ADR-0022); a session alone must not.
        if (_actor.UserId == userId)
        {
            throw new OwnAccountException("You cannot reset your own MFA here. Reset it from Profile, which first confirms it is you.");
        }

        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        await _recoveryCodes.StageRemovalOfAllAsync(user.Id, ct);
        user.TotpSecret = null;
        user.TotpEnabled = false;
        user.LastTotpStep = null;
        // A reset retires the account's authenticator, so it ends what the authenticator opened:
        // sessions and pending sign-ins are refused, as after the account's own reset.
        user.CredentialGeneration++;
        await _changes.CommitAsync(user, AdministratorEvent(AuditAction.UserTotpReset, user,
            $"MFA reset for '{user.Username}'; sessions revoked; TOTP re-enrollment required at next sign-in"), ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid userId, CancellationToken ct)
    {
        if (_actor.UserId == userId)
        {
            throw new OwnAccountException("You cannot delete your own account while signed in with it.");
        }

        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return false;
        }

        // Every request reads its session's account, so the account's sessions end when the account does.
        _users.StageRemoval(user);
        await CommitKeepingAnAdministratorAsync(user, IsEnabledAdministrator(user),
            AdministratorEvent(AuditAction.UserDeleted, user, $"Deleted '{user.Username}'; sessions revoked"),
            "Cannot delete the last TopSecret administrator — the vault would become unmanageable.", ct);
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
        // This is an authentication lifecycle event for the affected account, like AccountLockedOut;
        // keep it in that account's sign-in history. The User* administrative actions above instead
        // belong to the administrator's own activity.
        await _changes.CommitAsync(user, new AuditEntry
        {
            Action = AuditAction.AccountUnlocked,
            ResourceType = nameof(User),
            ResourceId = user.Id,
            ActorUserId = user.Id,
            InheritActorUserId = false,
            ActorUsername = _actor.Username ?? "system",
            Details = $"Lockout cleared for '{user.Username}'"
        }, ct);
        return true;
    }

    /// <summary>
    /// Commits a change to <paramref name="account"/>. A change that removes an enabled administrator
    /// commits only while another enabled administrator remains, counted under the vault-wide lock every
    /// such change takes, so two at once cannot both pass (ADR-0028).
    /// </summary>
    private Task CommitKeepingAnAdministratorAsync(User account, bool removesAnAdministrator, AuditEntry entry, string lastAdministratorRefusal, CancellationToken ct)
    {
        if (!removesAnAdministrator)
        {
            return _changes.CommitAsync(account, entry, ct);
        }

        return _changes.CommitAdministratorChangeAsync(account, entry, async token =>
        {
            if (!await _users.AnyOtherEnabledAdministratorAsync(account.Id, token))
            {
                throw new LastAdministratorException(lastAdministratorRefusal);
            }
        }, ct);
    }

    private static bool IsEnabledAdministrator(User account) => account.Clearance == ClearanceLevel.TopSecret && !account.IsDisabled;

    private UserSummaryDto Map(User u) =>
        new(u.Id, u.Username, u.DisplayName, u.Email, u.Clearance, u.ProjectKey,
            u.TotpEnabled, u.IsDisabled, u.IsLockedOut(_clock.GetUtcNow()), u.AvatarUpdatedAtUtc is not null, u.CreatedAtUtc);
}
