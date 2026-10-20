using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Models.Ui;

/// <param name="Clearance">The clearance's enum name, e.g. "TopSecret", as the export file writes it.</param>
public sealed record DataAccountResponse(
    string Username,
    string DisplayName,
    string Email,
    [property: UiEnum(typeof(ClearanceLevel))] string Clearance,
    string ProjectKey,
    bool HasCustomAvatar);
