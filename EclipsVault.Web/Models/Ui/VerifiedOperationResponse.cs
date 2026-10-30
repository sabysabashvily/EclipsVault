namespace EclipsVault.Web.Models.Ui;

/// <param name="CommitByUtc">The verification authorizes this operation's final action until this instant.</param>
public sealed record VerifiedOperationResponse(Guid OperationId, DateTime CommitByUtc);
