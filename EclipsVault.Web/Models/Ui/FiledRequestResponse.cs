using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Models.Ui;

/// <summary>A request the caller filed, in any status. The decision fields are null until it is decided.</summary>
public sealed record FiledRequestResponse(
    Guid RequestId,
    string SecretName,
    string Project,
    [property: UiEnum(typeof(AccessRequestStatus))] string Status,
    DateTime RequestedAtUtc,
    string? DecidedBy,
    DateTime? DecidedAtUtc,
    string? DecisionNote);
