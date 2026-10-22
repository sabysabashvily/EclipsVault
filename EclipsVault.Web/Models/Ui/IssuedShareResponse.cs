namespace EclipsVault.Web.Models.Ui;

public sealed record IssuedShareResponse(
    Guid ShareId,
    string SecretName,
    string Recipient,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc);
