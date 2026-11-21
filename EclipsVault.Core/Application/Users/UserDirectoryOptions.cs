namespace EclipsVault.Core.Application.Users;

/// <summary>
/// Directory settings for user provisioning. The email domain is combined with a
/// generated <c>first.last.N</c> local part until invitation-based creation replaces it;
/// <see cref="PublicUrl"/> is the sole origin used to build activation links (ADR-0033).
/// </summary>
public sealed record UserDirectoryOptions(string EmailDomain, string? PublicUrl = null)
{
    public static readonly UserDirectoryOptions Default = new("eclipsvault.local");
}
