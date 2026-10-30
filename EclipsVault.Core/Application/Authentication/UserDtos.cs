using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Authentication;

public sealed record UserDto(
    Guid Id,
    string Username,
    string DisplayName,
    string Email,
    ClearanceLevel Clearance,
    string ProjectKey,
    bool TotpEnabled,
    bool IsDisabled)
{
    /// <summary>
    /// The one place a <see cref="User"/> becomes a <see cref="UserDto"/>. It lives on the DTO
    /// rather than privately inside whichever service happens to need it, because a second copy is
    /// how the two disagree — and this is what a session's clearance and project are built from, so
    /// the copy that forgets a field is a copy that silently changes what someone may read.
    /// </summary>
    public static UserDto From(User user) =>
        new(user.Id, user.Username, user.DisplayName, user.Email,
            user.Clearance, user.ProjectKey, user.TotpEnabled, user.IsDisabled);
}

/// <summary>The password stage's outcome; a passed stage carries the stamp the rest of the sign-in must keep.</summary>
public sealed record CredentialCheckResult(CredentialStatus Status, UserDto? User, SessionStamp? Stamp)
{
    public static readonly CredentialCheckResult Invalid = new(CredentialStatus.Invalid, null, null);
}

public sealed record TotpEnrollmentDto(string SecretBase32, string OtpAuthUri);
