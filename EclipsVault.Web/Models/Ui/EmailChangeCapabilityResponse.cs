namespace EclipsVault.Web.Models.Ui;

public sealed record EmailChangeCapabilityResponse(bool Available, string? UnavailableReason, string? Explanation);
