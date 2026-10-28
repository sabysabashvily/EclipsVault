using System.Text.RegularExpressions;
using EclipsVault.Core.Domain;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Profile;

/// <summary>
/// Self-service profile management for the signed-in user. Cannot touch clearance or
/// project (those are administrative); every change commits together with its audit event.
/// </summary>
public sealed partial class ProfileService : IProfileService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IAvatarProcessor _avatar;
    private readonly IMfaRecoveryCodeRepository _recoveryCodes;
    private readonly IBreachedPasswordScreen _breachScreen;
    private readonly INotificationService _notifications;
    private readonly IAccountChangeCommitter _changes;
    private readonly IAuditSink _audit;
    private readonly IAuditContext _actor;

    public ProfileService(
        IUserRepository users,
        IPasswordHasher hasher,
        IAvatarProcessor avatar,
        IMfaRecoveryCodeRepository recoveryCodes,
        IBreachedPasswordScreen breachScreen,
        INotificationService notifications,
        IAccountChangeCommitter changes,
        IAuditSink audit,
        IAuditContext actor)
    {
        _users = users;
        _hasher = hasher;
        _avatar = avatar;
        _recoveryCodes = recoveryCodes;
        _breachScreen = breachScreen;
        _notifications = notifications;
        _changes = changes;
        _audit = audit;
        _actor = actor;
    }

    private Task AuditUserAsync(AuditAction action, Guid? userId, string username, string? details, CancellationToken ct)
        => _audit.WriteUserEventAsync(action, userId, username, details, ct);

    public async Task<ProfileDto?> GetAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        return user is null
            ? null
            : new ProfileDto(user.Id, user.Username, user.DisplayName, user.Email,
                user.Clearance, user.ProjectKey, user.TotpEnabled, user.AvatarUpdatedAtUtc is not null);
    }

    public async Task<ProfileUpdate> UpdateAsync(Guid userId, long credentialGeneration, string displayName, string email, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");
        user.RequireCredentialGeneration(credentialGeneration);

        displayName = ValidDisplayName(displayName);
        email = email.Trim();

        if (!EmailPattern().IsMatch(email))
        {
            throw new ProfileException("Enter a valid email address.");
        }

        if (await _users.IsEmailTakenAsync(email, user.Id, ct))
        {
            throw new EmailAddressTakenException();
        }

        // Email is a sign-in identity; a new spelling of the same address is not a new one.
        var emailChanged = user.NormalizedEmail != EmailIdentity.Normalize(email);
        user.DisplayName = displayName;
        user.Email = email;
        if (emailChanged)
        {
            user.CredentialGeneration++;
        }

        await _changes.CommitAsync(user, AuditAction.ProfileUpdated, "Display name / email updated", ct);

        var profile = new ProfileDto(user.Id, user.Username, user.DisplayName, user.Email,
            user.Clearance, user.ProjectKey, user.TotpEnabled, user.AvatarUpdatedAtUtc is not null);
        return new ProfileUpdate(profile, emailChanged, user.CredentialGeneration);
    }

    public async Task<string> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");

        user.DisplayName = ValidDisplayName(displayName);
        await _changes.CommitAsync(user, AuditAction.ProfileUpdated, "Display name updated", ct);
        return user.DisplayName;
    }

    public async Task<long> ChangePasswordAsync(
        Guid userId, long credentialGeneration, string currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");
        user.RequireCredentialGeneration(credentialGeneration);

        if (!_hasher.Verify(currentPassword, user.PasswordHash, user.PasswordSalt))
        {
            // Audited as an auth failure so repeated attempts are visible in the trail.
            await AuditUserAsync(AuditAction.LoginFailed, user.Id, user.Username, "Change-password: current password incorrect", ct);
            throw new ProfileException("Your current password is incorrect.");
        }

        if (newPassword.Length < PasswordRules.MinimumLength)
        {
            throw new ProfileException($"Your new password must be at least {PasswordRules.MinimumLength} characters long.");
        }

        if (newPassword == currentPassword)
        {
            throw new ProfileException("Your new password must be different from the current one.");
        }

        if (_breachScreen.IsCompromised(newPassword))
        {
            throw new ProfileException("This password has appeared in a known data breach. Choose a different one.");
        }

        var hashed = _hasher.Hash(newPassword);
        user.PasswordHash = hashed.Hash;
        user.PasswordSalt = hashed.Salt;
        // Committed with the password, so no session the old password opened survives the change,
        // whether or not the session store is reachable.
        user.CredentialGeneration++;
        await _changes.CommitAsync(user, AuditAction.PasswordChanged, "Password changed by owner; sessions revoked", ct);
        await _notifications.NotifyPasswordChangedAsync(user.Id, ct);
        return user.CredentialGeneration;
    }

    public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct)
        => _users.GetAvatarPngAsync(userId, ct);

    public async Task SetAvatarAsync(Guid userId, byte[] uploadedBytes, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");

        var png = _avatar.ProcessToPng(uploadedBytes);
        await _users.StageAvatarAsync(user, png, ct);
        await _changes.CommitAsync(user, AuditAction.AvatarUpdated, "Profile picture updated", ct);
    }

    public async Task RemoveAvatarAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");

        await _users.StageAvatarRemovalAsync(user, ct);
        await _changes.CommitAsync(user, AuditAction.AvatarRemoved, "Profile picture removed", ct);
    }

    public async Task ResetOwnMfaAsync(Guid userId, long credentialGeneration, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");
        user.RequireCredentialGeneration(credentialGeneration);

        // Load the rows before changing the account, so a failed query leaves no reset staged.
        await _recoveryCodes.StageRemovalOfAllAsync(user.Id, ct);
        user.TotpSecret = null;
        user.TotpEnabled = false;
        // The replay marker belongs to the retired authenticator, so it goes with it.
        user.LastTotpStep = null;
        user.CredentialGeneration++;
        await _changes.CommitAsync(user, AuditAction.SelfMfaReset, "Authenticator reset by owner; re-enrollment required", ct);
    }

    private static string ValidDisplayName(string displayName)
    {
        displayName = displayName.Trim();
        return displayName.Length is < 1 or > 64
            ? throw new ProfileException("Display name must be between 1 and 64 characters.")
            : displayName;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
