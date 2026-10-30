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

/// <summary>Cosmetic Profile edits and shared credential persistence for remaining authentication callers.</summary>
public interface IProfileService
{
    Task<ProfileDto?> GetAsync(Guid userId, CancellationToken ct);

    /// <summary>Changes the display name alone. Throws ProfileException on invalid input.</summary>
    /// <returns>The display name as saved.</returns>
    Task<string> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken ct);

    /// <summary>Verifies the current password before setting a new one. Throws ProfileException on mismatch or a weak password.</summary>
    /// <returns>The credential generation the change committed.</returns>
    Task<long> ChangePasswordAsync(Guid userId, long credentialGeneration, string currentPassword, string newPassword, CancellationToken ct);

    Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct);

    /// <summary>Validates and re-encodes the uploaded image to a safe PNG, then stores it. Throws ProfileException if unreadable.</summary>
    Task SetAvatarAsync(Guid userId, byte[] uploadedBytes, CancellationToken ct);

    Task RemoveAvatarAsync(Guid userId, CancellationToken ct);

    /// <summary>Clears the user's TOTP so they re-enroll a fresh authenticator at next sign-in.</summary>
    Task ResetOwnMfaAsync(Guid userId, long credentialGeneration, CancellationToken ct);
}
