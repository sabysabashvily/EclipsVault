namespace EclipsVault.Web.Models.Ui;

public sealed record SharedByMeResponse(IReadOnlyList<IssuedShareResponse> Shares);
