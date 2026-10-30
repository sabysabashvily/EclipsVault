namespace EclipsVault.Web.Models.Ui;

public sealed record PendingEmailResponse(Guid OperationId, string Address, DateTime ExpiresAtUtc);
