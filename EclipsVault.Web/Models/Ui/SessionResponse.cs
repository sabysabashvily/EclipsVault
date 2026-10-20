namespace EclipsVault.Web.Models.Ui;

public sealed record SessionResponse(
    Guid SessionId,
    string Device,
    string IpAddress,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    bool IsCurrent);
