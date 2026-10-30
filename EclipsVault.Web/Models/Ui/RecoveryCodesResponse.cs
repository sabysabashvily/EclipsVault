namespace EclipsVault.Web.Models.Ui;

/// <summary>The ten new recovery codes. This response is the only time they exist in plaintext.</summary>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> Codes);
