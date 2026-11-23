using System.ComponentModel.DataAnnotations;

namespace EclipsVault.Web.Models.Ui;

public sealed record ClearanceOptionResponse(int Value, string Label);

public sealed record NewUserResponse(IReadOnlyList<ClearanceOptionResponse> Clearances, bool InvitationsAvailable);

public sealed record UserDetailsResponse(Guid UserId, string Username, string DisplayName, string Email, int Clearance,
    string Project, bool IsYou, bool IsDisabled, IReadOnlyList<ClearanceOptionResponse> Clearances,
    bool AwaitingActivation, DateTimeOffset? InvitationExpiresAtUtc, bool InvitationsAvailable);

public sealed record CreatedUserResponse(Guid UserId, string Username, string Email,
    [property: AllowedValues("sent", "not_delivered")] string Invitation, DateTimeOffset ExpiresAtUtc);

public sealed record InvitationResponse([property: AllowedValues("sent", "not_delivered")] string Invitation, DateTimeOffset ExpiresAtUtc);

public sealed record InvitationAddressResponse(string Email);

public sealed class ReplaceInvitationAddressRequest
{
    public string? Email { get; init; }
}

public sealed class CreateUserUiRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Username { get; init; }
    public string? Email { get; init; }
    public int? Clearance { get; init; }
    public string? Project { get; init; }
}

public sealed class UserRoleRequest
{
    public int? Clearance { get; init; }
    public string? Project { get; init; }
}
