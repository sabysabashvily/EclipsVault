using System.Text.Json.Serialization;

namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// How long the share an approval issues lasts, in whole days; 0 means it never expires. Null is refused
/// with the out-of-range answer, so a missing value never approves a share without expiry.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccessApprovalRequest(int? ExpiresInDays);
