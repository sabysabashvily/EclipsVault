using System.Text.Json;

namespace EclipsVault.Web.Models;

/// <summary>Body posted by the browser to complete a passkey registration ceremony.</summary>
public sealed class PasskeyRegistrationCompletion
{
    /// <summary>Optional friendly label for the new passkey.</summary>
    public string? Nickname { get; set; }

    /// <summary>The raw PublicKeyCredential the authenticator produced (id + attestation response).</summary>
    public JsonElement Credential { get; set; }
}

/// <summary>Optional body posted to begin a passkey sign-in; a username scopes the allowed credentials.</summary>
public sealed class PasskeyLoginRequest
{
    public string? Username { get; set; }

    public string? ReturnUrl { get; set; }
}

/// <summary>Body posted by the browser to complete a passkey sign-in ceremony.</summary>
public sealed class PasskeyAssertionCompletion
{
    /// <summary>The raw PublicKeyCredential the authenticator produced (id + assertion response).</summary>
    public JsonElement Credential { get; set; }
}
