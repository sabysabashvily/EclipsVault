using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Models.Ui;

/// <param name="ExpiresAtUtc">The operation must be verified before this instant.</param>
/// <param name="Methods">The methods that can verify this operation.</param>
public sealed record PreparedOperationResponse(
    Guid OperationId,
    [property: UiEnum(typeof(ProfileOperationKind))] string Kind,
    DateTime ExpiresAtUtc,
    [property: UiEnum(typeof(AccountProofMethod))] IReadOnlyList<string> Methods);
