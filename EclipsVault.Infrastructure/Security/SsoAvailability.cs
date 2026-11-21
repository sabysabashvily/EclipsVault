using Microsoft.Extensions.Options;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// Adapts <see cref="SsoOptions"/> to <see cref="ISsoAvailability"/>, handing the presentation layer
/// the fields the sign-in page needs and nothing else — notably not <see cref="SsoOptions.ClientSecret"/>.
/// Reads through to the options monitor rather than snapshotting, so the port never becomes a stale
/// copy of configuration.
/// </summary>
public sealed class SsoAvailability : ISsoAvailability
{
    private readonly IOptions<SsoOptions> _options;

    public SsoAvailability(IOptions<SsoOptions> options) => _options = options;

    public bool Enabled => _options.Value.Enabled;

    public string DisplayName => _options.Value.DisplayName;

    // Scheme, ASCII host and port only, so neither a configured path or query nor a non-ASCII
    // character ever reaches a policy header.
    public string IdentityProviderOrigin =>
        _options.Value.Enabled
        && Uri.TryCreate(_options.Value.Authority, UriKind.Absolute, out var authority)
        && (authority.Scheme == Uri.UriSchemeHttps || authority.Scheme == Uri.UriSchemeHttp)
            ? new UriBuilder(authority.Scheme, authority.IdnHost, authority.Port).Uri.GetComponents(
                UriComponents.SchemeAndServer, UriFormat.UriEscaped)
            : string.Empty;
}
