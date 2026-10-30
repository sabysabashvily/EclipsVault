namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// Whether a change can be started, and if not, the same stable code its refusal carries:
/// <c>method_unavailable</c>, <c>authenticator_not_enrolled</c> or <c>alternate_method_required</c>.
/// </summary>
public sealed record ProfileActionResponse(bool Available, string? UnavailableReason);
