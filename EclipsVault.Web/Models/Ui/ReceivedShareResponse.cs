using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// One secret shared with the caller. <see cref="Sensitivity"/> is the classification's rank, which
/// selects the existing badge style; <see cref="SensitivityLabel"/> is what the badge says.
/// </summary>
public sealed record ReceivedShareResponse(
    Guid SecretId,
    string Name,
    string Project,
    [property: UiEnum(typeof(SecretEnvironment))] string Environment,
    int Sensitivity,
    string SensitivityLabel,
    string SharedBy,
    DateTime? AccessUntilUtc);
