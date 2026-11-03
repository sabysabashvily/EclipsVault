using System.ComponentModel.DataAnnotations;

namespace EclipsVault.Web.Models.Ui;

public sealed record UserResponse(
    Guid UserId,
    string DisplayName,
    string Username,
    string Email,
    int Clearance,
    string ClearanceLabel,
    string Project,
    bool MfaEnrolled,
    [property: AllowedValues("active", "locked", "disabled")] string Status,
    string AvatarUrl,
    bool IsYou);
