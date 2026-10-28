using System.Text.Json;
using System.Text.Json.Serialization;

namespace EclipsVault.Web.Models.Ui;

/// <summary>The new passkey's registration response, for the verified operation that adds it.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasskeyRegistrationRequest(Guid OperationId, JsonElement Credential);
