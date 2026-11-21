namespace EclipsVault.Web.Models;

public sealed class AccessDeniedViewModel
{
    /// <summary>Why the ABAC policy rejected the request; empty when the page is reached without context.</summary>
    public IReadOnlyList<string> Reasons { get; set; } = [];

    /// <summary>The secret that was denied, when the page was reached from a secret access attempt — enables the "Request access" form.</summary>
    public Guid? SecretId { get; set; }

    /// <summary>Why a request for <see cref="SecretId"/> would be refused, shown instead of the form; null when it would be filed.</summary>
    public string? RequestRefusalMessage { get; set; }
}
