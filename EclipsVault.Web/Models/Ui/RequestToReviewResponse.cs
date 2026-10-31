namespace EclipsVault.Web.Models.Ui;

/// <summary>A pending request the caller may decide. <see cref="DeniedBecause"/> is the vault's own access check, or null.</summary>
public sealed record RequestToReviewResponse(
    Guid RequestId,
    string SecretName,
    string Project,
    string Requester,
    string Reason,
    string? DeniedBecause,
    DateTime RequestedAtUtc);
