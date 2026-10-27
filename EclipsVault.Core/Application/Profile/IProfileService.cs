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

/// <summary>
/// A saved details change. Changing the email changes how the account signs in, so it advanced the
/// credential generation and the caller's session must be replaced under <see cref="CredentialGeneration"/>.
/// </summary>
public sealed record ProfileUpdate(ProfileDto Profile, bool EmailChanged, long CredentialGeneration);

/// <summary>
/// Self-service account management for the signed-in user: display name and email,
/// profile picture, password, and their own MFA enrollment. Every mutation is
/// audited. Nothing here can change a user's clearance or project — that is an
/// administrative action (see <see cref="IUserAdminService"/>).
///
/// A change to how the account signs in takes the credential generation of the caller's session,
/// throws <see cref="Domain.Exceptions.StaleProofException"/> when the account has moved on, and
/// advances the generation in the same transaction, so every session issued before it is refused.
/// </summary>
public interface IProfileService
{
    Task<ProfileDto?> GetAsync(Guid userId, CancellationToken ct);

    /// <summary>Updates display name and email. Throws ProfileException on invalid input.</summary>
    Task<ProfileUpdate> UpdateAsync(Guid userId, long credentialGeneration, string displayName, string email, CancellationToken ct);

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
