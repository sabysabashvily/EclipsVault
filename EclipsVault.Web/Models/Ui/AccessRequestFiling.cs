using System.Text.Json.Serialization;

namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// Whether the Denied page offers the request form. When it does not, <see cref="Refusal"/> is the
/// refusal's code and <see cref="Message"/> the sentence the page shows instead.
/// </summary>
public sealed record AccessRequestEligibilityResponse(bool Eligible, string? Refusal, string? Message);

/// <summary>A request to be shared a secret, filed from the Denied page.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccessRequestFilingRequest(Guid SecretId, string? Reason);
