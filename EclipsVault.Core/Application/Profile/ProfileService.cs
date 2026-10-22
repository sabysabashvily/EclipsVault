using System.Text.RegularExpressions;
using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Profile;

/// <summary>
/// Self-service profile management for the signed-in user. Cannot touch clearance or
/// project (those are administrative); every change is written to the audit trail.
/// </summary>
public sealed partial class ProfileService : IProfileService
{
    private const int MinimumPasswordLength = 12;

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IAvatarProcessor _avatar;
    private readonly IMfaRecoveryCodeRepository _recoveryCodes;
    private readonly IBreachedPasswordScreen _breachScreen;
    private readonly INotificationService _notifications;
    private readonly ISessionRevocationService _revocation;
    private readonly IAuditSink _audit;
    private readonly IAuditContext _actor;
    private readonly TimeProvider _clock;

    public ProfileService(
        IUserRepository users,
        IPasswordHasher hasher,
        IAvatarProcessor avatar,
        IMfaRecoveryCodeRepository recoveryCodes,
        IBreachedPasswordScreen breachScreen,
        INotificationService notifications,
        ISessionRevocationService revocation,
        IAuditSink audit,
        IAuditContext actor,
        TimeProvider clock)
    {
        _users = users;
        _hasher = hasher;
        _avatar = avatar;
        _recoveryCodes = recoveryCodes;
        _breachScreen = breachScreen;
        _notifications = notifications;
        _revocation = revocation;
        _audit = audit;
        _actor = actor;
        _clock = clock;
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

    public async Task<ProfileDto> UpdateAsync(Guid userId, string displayName, string email, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");

        displayName = displayName.Trim();
        email = email.Trim();

        if (displayName.Length is < 1 or > 64)
        {
            throw new ProfileException("Display name must be between 1 and 64 characters.");
        }

        if (!EmailPattern().IsMatch(email))
        {
            throw new ProfileException("Enter a valid email address.");
        }

        if (await _users.IsEmailTakenAsync(email, user.Id, ct))
        {
            throw new EmailAddressTakenException();
        }

        user.DisplayName = displayName;
        user.Email = email;
        await _users.UpdateAsync(user, ct);
        await AuditUserAsync(AuditAction.ProfileUpdated, user.Id, user.Username, "Display name / email updated", ct);

        return new ProfileDto(user.Id, user.Username, user.DisplayName, user.Email,
            user.Clearance, user.ProjectKey, user.TotpEnabled, user.AvatarUpdatedAtUtc is not null);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");

        if (!_hasher.Verify(currentPassword, user.PasswordHash, user.PasswordSalt))
        {
            // Audited as an auth failure so repeated attempts are visible in the trail.
            await AuditUserAsync(AuditAction.LoginFailed, user.Id, user.Username, "Change-password: current password incorrect", ct);
            throw new ProfileException("Your current password is incorrect.");
        }

        if (newPassword.Length < MinimumPasswordLength)
        {
            throw new ProfileException($"Your new password must be at least {MinimumPasswordLength} characters long.");
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
        await _users.UpdateAsync(user, ct);

        // A changed password must not leave a session opened with the old one alive. Revoke every
        // session issued up to now — this device included; the caller's own cookie is then re-issued
        // with a later strong-auth time by the controller, so only the *other* devices are turned
        // out. Doing it here rather than at the call site keeps a stolen session from surviving a
        // password reset no matter which entry point triggers the change.
        await _revocation.RevokeAsync(user.Id, _clock.GetUtcNow(), ct);

        await AuditUserAsync(AuditAction.PasswordChanged, user.Id, user.Username, "Password changed by owner; sessions revoked", ct);
        await _notifications.NotifyPasswordChangedAsync(user.Id, ct);
    }

    public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct)
        => _users.GetAvatarPngAsync(userId, ct);

    public async Task SetAvatarAsync(Guid userId, byte[] uploadedBytes, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");

        var png = _avatar.ProcessToPng(uploadedBytes); // validates + re-encodes; throws ProfileException if unreadable
        await _users.SetAvatarAsync(user, png, ct);
        await AuditUserAsync(AuditAction.AvatarUpdated, user.Id, user.Username, "Profile picture updated", ct);
    }

    public async Task RemoveAvatarAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");

        await _users.RemoveAvatarAsync(user, ct);
        await AuditUserAsync(AuditAction.AvatarRemoved, user.Id, user.Username, "Profile picture removed", ct);
    }

    public async Task ResetOwnMfaAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");

        user.TotpSecret = null;
        user.TotpEnabled = false;
        await _users.UpdateAsync(user, ct);
        // The authenticator is gone, so its recovery codes must not remain a live second factor.
        await _recoveryCodes.DeleteAllAsync(user.Id, ct);
        await AuditUserAsync(AuditAction.SelfMfaReset, user.Id, user.Username, "Authenticator reset by owner; re-enrollment required", ct);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
