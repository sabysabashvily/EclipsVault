using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Profile;

/// <summary>
/// Self-service profile management for the signed-in user. Cannot touch clearance or
/// project (those are administrative); every change commits together with its audit event.
/// </summary>
public sealed class ProfileService : IProfileService
{
    private readonly IUserRepository _users;
    private readonly IAvatarProcessor _avatar;
    private readonly IAccountChangeCommitter _changes;

    public ProfileService(IUserRepository users, IAvatarProcessor avatar, IAccountChangeCommitter changes)
    {
        _users = users;
        _avatar = avatar;
        _changes = changes;
    }

    public async Task<ProfileDto?> GetAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        return user is null
            ? null
            : new ProfileDto(user.Id, user.Username, user.DisplayName, user.Email,
                user.Clearance, user.ProjectKey, user.TotpEnabled, user.AvatarUpdatedAtUtc is not null);
    }

    public async Task<string> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new ProfileException("Your account could not be loaded.");

        user.DisplayName = ValidDisplayName(displayName);
        await _changes.CommitAsync(user, AuditAction.ProfileUpdated, "Display name updated", ct);
        return user.DisplayName;
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

    private static string ValidDisplayName(string displayName)
    {
        displayName = displayName.Trim();
        return displayName.Length is < 1 or > 64
            ? throw new ProfileException("Display name must be between 1 and 64 characters.")
            : displayName;
    }

}
