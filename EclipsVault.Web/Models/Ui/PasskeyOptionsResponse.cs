using System.Text.Json;

namespace EclipsVault.Web.Models.Ui;

/// <summary>The options to pass to <c>navigator.credentials</c> as its <c>publicKey</c>.</summary>
public sealed record PasskeyOptionsResponse(JsonElement PublicKey);
