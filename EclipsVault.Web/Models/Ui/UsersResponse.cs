namespace EclipsVault.Web.Models.Ui;

/// <param name="InvitationsAvailable">Whether Resend can deliver: real mail delivery and <c>Identity:PublicUrl</c> are configured.</param>
public sealed record UsersResponse(IReadOnlyList<UserResponse> Users, bool InvitationsAvailable);
