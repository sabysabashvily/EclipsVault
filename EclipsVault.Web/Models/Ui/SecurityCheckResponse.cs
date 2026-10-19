namespace EclipsVault.Web.Models.Ui;

/// <summary>One control of the checkup, in Core's most-urgent-first order. <c>Fix</c> names where it is fixed, not a URL.</summary>
public sealed record SecurityCheckResponse(
    [property: UiEnum(typeof(SecurityCheckKind))] string Kind,
    [property: UiEnum(typeof(SecurityCheckStatus))] string Status,
    string Title,
    string Detail,
    string Recommendation,
    [property: UiEnum(typeof(RemediationArea))] string Fix);
