using System.Text.Json.Serialization;

namespace EclipsVault.Web.Models.Ui;

/// <summary>The display name, the only detail saved without account verification.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfileDetailsRequest(string DisplayName);
