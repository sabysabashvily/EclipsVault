using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Profile;

public sealed record ProfileDto(
    Guid Id,
    string Username,
    string DisplayName,
    string Email,
    ClearanceLevel Clearance,
    string ProjectKey,
    bool TotpEnabled,
    bool HasCustomAvatar);

/// <summary>Cosmetic Profile edits: the display name and the profile picture.</summary>
public interface IProfileService
{
    Task<ProfileDto?> GetAsync(Guid userId, CancellationToken ct);

    /// <summary>Changes the display name alone. Throws ProfileException on invalid input.</summary>
    /// <returns>The display name as saved.</returns>
    Task<string> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct);

    Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct);

    /// <summary>Validates and re-encodes the uploaded image to a safe PNG, then stores it. Throws ProfileException if unreadable.</summary>
    Task SetAvatarAsync(Guid userId, byte[] uploadedBytes, CancellationToken ct);

    Task RemoveAvatarAsync(Guid userId, CancellationToken ct);
}
