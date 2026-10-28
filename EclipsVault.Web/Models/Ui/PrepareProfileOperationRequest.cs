using System.Text.Json.Serialization;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// One intended security change, tagged by <paramref name="Kind"/>. Only that kind's field may be set:
/// <paramref name="NewPassword"/> to change the password, <paramref name="PasskeyNickname"/> to add a
/// passkey, <paramref name="PasskeyId"/> to remove one.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareProfileOperationRequest(
    [property: UiEnum(typeof(ProfileOperationKind))] string Kind,
    string? NewPassword = null,
    string? PasskeyNickname = null,
    Guid? PasskeyId = null);
