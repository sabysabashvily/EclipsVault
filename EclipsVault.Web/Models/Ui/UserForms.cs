namespace EclipsVault.Web.Models.Ui;

public sealed record ClearanceOptionResponse(int Value, string Label);

public sealed record NewUserResponse(string EmailDomain, IReadOnlyList<ClearanceOptionResponse> Clearances);

public sealed record UserDetailsResponse(Guid UserId, string Username, string DisplayName, int Clearance,
    string Project, bool IsYou, IReadOnlyList<ClearanceOptionResponse> Clearances);

public sealed record CreatedUserResponse(Guid UserId, string Username, string Email);

public sealed class CreateUserUiRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public int? Clearance { get; init; }
    public string? Project { get; init; }
}

public sealed class UserRoleRequest
{
    public int? Clearance { get; init; }
    public string? Project { get; init; }
}
