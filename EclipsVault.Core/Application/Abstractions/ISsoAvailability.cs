namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Whether this deployment offers single sign-on, and what the button should say.
///
/// <para>
/// This exists so the sign-in page can render the SSO button without depending on the OIDC wiring
/// that produces the answer. That is a layering point and a secrecy one: the configuration object
/// behind this also carries the client secret, and the presentation layer has no business holding a
/// type with a credential on it merely to read a display name. What the sign-in page needs is the
/// whole contract.
/// </para>
/// </summary>
public interface ISsoAvailability
{
    /// <summary>False unless SSO is configured, in which case no SSO affordance should be shown.</summary>
    bool Enabled { get; }

    /// <summary>The identity provider's display name, e.g. "Okta" — what the sign-in button reads.</summary>
    string DisplayName { get; }

    /// <summary>
    /// The identity provider's origin, e.g. <c>https://idp.example.com</c>, where the sign-in form
    /// redirects; empty unless SSO is enabled with an http or https authority.
    /// </summary>
    string IdentityProviderOrigin { get; }
}
