using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Users;

public sealed record UserSummaryDto(
    Guid Id,
    string Username,
    string DisplayName,
    string Email,
    ClearanceLevel Clearance,
    string ProjectKey,
    bool TotpEnabled,
    bool IsDisabled,
    bool IsLockedOut,
    bool HasCustomAvatar,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateUserRequest(
    string Username,
    string FirstName,
    string LastName,
    string Password,
    ClearanceLevel Clearance,
    string ProjectKey);

/// <summary>Result of provisioning, including the email address generated for the account.</summary>
public sealed record CreatedUserDto(Guid Id, string Username, string Email, string DisplayName);

/// <summary>
/// Administrative user lifecycle: provisioning, role and access control, MFA reset,
/// forced logout, disable/enable, and removal. Every operation is audited. Values outside the
/// account input rules are refused with <see cref="AccountInputException"/>, changes Profile governs
/// on the caller's own account with <see cref="OwnAccountException"/>, and a change that would leave
/// no enabled administrator with <see cref="LastAdministratorException"/>.
/// </summary>
public interface IUserAdminService
{
    Task<IReadOnlyList<UserSummaryDto>> ListAsync(CancellationToken ct);

    Task<UserSummaryDto?> GetAsync(Guid userId, CancellationToken ct);

    /// <summary>Provisions a user, auto-generating a unique <c>first.last.N@domain</c> email.</summary>
    Task<CreatedUserDto> CreateAsync(CreateUserRequest request, CancellationToken ct);

    /// <summary>Changes a user's clearance and project assignment, ending the user's sessions.</summary>
    Task<bool> SetRoleAsync(Guid userId, ClearanceLevel clearance, string projectKey, CancellationToken ct);

    /// <summary>Enables or disables an account, ending its sessions.</summary>
    Task<bool> SetEnabledAsync(Guid userId, bool enabled, CancellationToken ct);

    /// <summary>Immediately revokes every active session for the user (server-side kill switch).</summary>
    Task<bool> ForceLogoutAsync(Guid userId, CancellationToken ct);

    /// <summary>Clears TOTP state so the user re-enrolls at next sign-in (lost authenticator, device change).</summary>
    Task<bool> ResetTotpAsync(Guid userId, CancellationToken ct);

    /// <summary>Clears a brute-force lockout and resets the failed-attempt counter.</summary>
    Task<bool> UnlockAsync(Guid userId, CancellationToken ct);

    Task<bool> DeleteAsync(Guid userId, CancellationToken ct);
}
