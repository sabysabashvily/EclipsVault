using System.Net.Mail;
using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Users;

/// <summary>
/// Administrative user lifecycle. New accounts are invited at the person's own email and hold no
/// credential until the person activates them (ADR-0033); role changes, disabling, and deletions are
/// guarded against locking the vault out of its own administration.
/// </summary>
public sealed class UserAdminService : IUserAdminService
{
    private const int MaximumEmailLength = 254;
    private const int MaximumProjectKeyLength = 64;
    private const int MaximumNameLength = 64;

    /// <summary>The display name column's width, which "First Last" must fit.</summary>
    private const int MaximumDisplayNameLength = 64;

    private const string InvitationsUnavailableMessage =
        "Invitations need email delivery and the vault's public address (Identity:PublicUrl). Configure both to add accounts.";

    private readonly IUserRepository _users;
    private readonly IAccountInvitationRepository _invitations;
    private readonly IAccountInvitationDelivery _invitationDelivery;
    private readonly ISessionRevocationService _revocation;
    private readonly IMfaRecoveryCodeRepository _recoveryCodes;
    private readonly IAuditSink _audit;
    private readonly IAccountChangeCommitter _changes;
    private readonly IAuditContext _actor;
    private readonly TimeProvider _clock;

    public UserAdminService(
        IUserRepository users,
        IAccountInvitationRepository invitations,
        IAccountInvitationDelivery invitationDelivery,
        ISessionRevocationService revocation,
        IMfaRecoveryCodeRepository recoveryCodes,
        IAuditSink audit,
        IAccountChangeCommitter changes,
        IAuditContext actor,
        TimeProvider clock)
    {
        _users = users;
        _invitations = invitations;
        _invitationDelivery = invitationDelivery;
        _revocation = revocation;
        _recoveryCodes = recoveryCodes;
        _audit = audit;
        _changes = changes;
        _actor = actor;
        _clock = clock;
    }

    public bool InvitationsAvailable => _invitationDelivery.Available;

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
        CheckNewAccount(username, first, last, displayName);
        var email = CheckedEmail(request.Email);
        CheckClearance(request.Clearance);
        var project = CheckedProjectKey(request.ProjectKey);
        if (!_invitationDelivery.Available)
        {
            throw new InvitationsUnavailableException(InvitationsUnavailableMessage);
        }

        if (await _users.FindByUsernameAsync(username, ct) is not null)
        {
            throw new UsernameTakenException(username);
        }

        // The account is not saved yet, so no account's own address is exempt.
        if (await _users.IsEmailTakenAsync(email, Guid.Empty, ct))
        {
            throw new EmailAddressTakenException();
        }

        var now = _clock.GetUtcNow();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = displayName,
            Email = email,
            Clearance = request.Clearance,
            ProjectKey = project,
            AwaitingActivation = true,
            CreatedAtUtc = now
        };
        var token = InvitationToken.Generate();
        var expiresAtUtc = now + InvitationToken.Lifetime;
        _users.StageAddition(user);
        _invitations.StageAddition(new AccountInvitation
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = InvitationToken.Hash(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        });

        try
        {
            await _changes.CommitAsync(user, AdministratorEvent(AuditAction.UserCreated, user,
                $"Created '{user.Username}' <{user.Email}> (clearance {user.Clearance}, project {user.ProjectKey})"), ct);
        }
        catch (UsernameTakenException)
        {
            throw new UsernameTakenException(user.Username);
        }
        catch (EmailAddressTakenException)
        {
            // The same create sent twice at once loses on both indexes, and the database may report the
            // address first. The username is what the administrator chose, so name it.
            if (await _users.FindByUsernameAsync(user.Username, ct) is not null)
            {
                throw new UsernameTakenException(user.Username);
            }

            throw;
        }

        var delivered = await _invitationDelivery.TrySendAsync(user.Email, displayName, user.Username, token, ct);
        return new CreatedUserDto(user.Id, user.Username, user.Email, displayName, new SentInvitationDto(delivered, expiresAtUtc));
    }

    public async Task<SentInvitationDto?> ResendInvitationAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return null;
        }

        if (!user.AwaitingActivation)
        {
            throw new AccountAlreadyActiveException("This account has already been activated.");
        }

        if (!_invitationDelivery.Available)
        {
            throw new InvitationsUnavailableException(InvitationsUnavailableMessage);
        }

        var existing = await _invitations.FindByUserIdAsync(user.Id, ct);
        var token = InvitationToken.Generate();
        var tokenHash = InvitationToken.Hash(token);
        var now = _clock.GetUtcNow();
        var expiresAtUtc = now + InvitationToken.Lifetime;

        if (existing is null)
        {
            _invitations.StageAddition(new AccountInvitation
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = tokenHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAtUtc
            });
        }
        else
        {
            existing.TokenHash = tokenHash;
            existing.CreatedAtUtc = now;
            existing.ExpiresAtUtc = expiresAtUtc;
            existing.SpentAtUtc = null;
            existing.PendingTotpSecret = null;
        }

        await _changes.CommitAsync(user, AdministratorEvent(AuditAction.InvitationSent, user,
            $"Invitation issued for '{user.Username}' to <{user.Email}>; expires {expiresAtUtc:u}"), ct);

        var delivered = await _invitationDelivery.TrySendAsync(user.Email, user.DisplayName, user.Username, token, ct);
        return new SentInvitationDto(delivered, expiresAtUtc);
    }

    public async Task<string?> ReplaceInvitationAddressAsync(Guid userId, string email, CancellationToken ct)
    {
        var trimmed = CheckedEmail(email);
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return null;
        }

        if (!user.AwaitingActivation)
        {
            throw new AccountAlreadyActiveException("This account has already been activated.");
        }

        if (await _users.IsEmailTakenAsync(trimmed, user.Id, ct))
        {
            throw new EmailAddressTakenException();
        }

        var existing = await _invitations.FindByUserIdAsync(user.Id, ct);
        var hasUnspentToken = existing is not null && existing.SpentAtUtc is null;
        if (string.Equals(user.Email, trimmed, StringComparison.Ordinal) && !hasUnspentToken)
        {
            return user.Email;
        }

        user.Email = trimmed;
        if (existing is not null && existing.SpentAtUtc is null)
        {
            // Spend the live token in the same transaction so the link already sent cannot activate the account.
            existing.SpentAtUtc = _clock.GetUtcNow();
            existing.PendingTotpSecret = null;
        }

        await _changes.CommitAsync(user,
            AdministratorEvent(AuditAction.InvitationAddressReplaced, user,
                $"Invitation address for '{user.Username}' replaced with <{user.Email}>"),
            async token =>
            {
                if (await _users.IsEmailTakenAsync(trimmed, user.Id, token))
                {
                    throw new EmailAddressTakenException();
                }
            }, ct);

        return user.Email;
    }

    private static string CheckedEmail(string email)
    {
        var trimmed = email.Trim();
        if (trimmed.Length is 0 or > MaximumEmailLength
            || trimmed.Any(char.IsControl)
            || !MailAddress.TryCreate(trimmed, out var parsed)
            || !string.Equals(parsed.Address, trimmed, StringComparison.Ordinal))
        {
            throw new AccountInputException("Email", "Enter a valid email address.");
        }

        return trimmed;
    }

    /// <summary>
    /// The New user form's rules for the username and names, so callers other than the form are held to
    /// them too. Email, clearance and project key have checks of their own, shared with other changes.
    /// </summary>
    private static void CheckNewAccount(string username, string first, string last, string displayName)
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
            return true;
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

        if (user.AwaitingActivation)
        {
            throw new AccountNotActivatedException("This account has not been activated yet.");
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
            return true;
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

    private static bool IsEnabledAdministrator(User account)
        => account.Clearance == ClearanceLevel.TopSecret && !account.IsDisabled && !account.AwaitingActivation;

    private UserSummaryDto Map(User u) =>
        new(u.Id, u.Username, u.DisplayName, u.Email, u.Clearance, u.ProjectKey,
            u.TotpEnabled, u.IsDisabled, u.IsLockedOut(_clock.GetUtcNow()), u.AvatarUpdatedAtUtc is not null, u.CreatedAtUtc);
}
