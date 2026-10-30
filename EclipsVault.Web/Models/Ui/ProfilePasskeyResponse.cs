namespace EclipsVault.Web.Models.Ui;

/// <summary>A registered passkey by its record id; its authenticator-issued credential id stays on the server.</summary>
public sealed record ProfilePasskeyResponse(Guid Id, string Nickname, DateTime CreatedAtUtc, ProfileActionResponse Removal);
