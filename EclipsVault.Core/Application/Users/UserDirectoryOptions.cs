namespace EclipsVault.Core.Application.Users;

/// <summary>
/// Directory settings for user provisioning. <see cref="PublicUrl"/> is the sole origin used to build
/// activation links (ADR-0033).
/// </summary>
public sealed record UserDirectoryOptions(string? PublicUrl);
