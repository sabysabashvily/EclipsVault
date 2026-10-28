using System.Text.Json;
using System.Text.Json.Serialization;

namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// A passkey's answer to the operation's prompt, as the browser returned it. A password change also
/// needs <paramref name="CurrentPassword"/>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasskeyProofRequest(JsonElement Credential, string? CurrentPassword = null);
