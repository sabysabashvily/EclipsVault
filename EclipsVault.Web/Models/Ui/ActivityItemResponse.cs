namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// One activity entry, as the Legacy UI showed it. The audit row's id, username, details, resource
/// type, raw action and critical flag stay on the server.
/// </summary>
public sealed record ActivityItemResponse(
    DateTime TimestampUtc,
    [property: UiEnum(typeof(ActivityCategory))] string Category,
    string Title,
    [property: UiEnum(typeof(ActivitySeverity))] string Severity,
    string? ResourceName,
    string SourceIp);
