using System.ComponentModel.DataAnnotations;

namespace EclipsVault.Web.Models.Ui;

public sealed record LicenseResponse(
    [property: AllowedValues("Missing", "Malformed", "InvalidSignature", "Expired", "Valid")] string Status,
    string Message, LicenseDetailsResponse? Licence, bool UpdatesLapsed, IReadOnlyList<string> BeyondTier);

public sealed record LicenseDetailsResponse(string IssuedTo, string Tier, string? Contact, string LicenseId,
    DateTimeOffset IssuedAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? UpdatesUntilUtc, int MaxNodes);
