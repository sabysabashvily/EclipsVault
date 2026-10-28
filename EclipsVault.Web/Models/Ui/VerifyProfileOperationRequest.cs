using System.Text.Json.Serialization;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Models.Ui;

/// <summary>The current password with an authenticator code or one unused recovery code.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VerifyProfileOperationRequest(
    [property: UiEnum(typeof(AccountProofMethod))] string Method,
    string CurrentPassword,
    string Code);
