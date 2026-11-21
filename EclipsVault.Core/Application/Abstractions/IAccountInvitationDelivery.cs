namespace EclipsVault.Core.Application.Abstractions;

public interface IAccountInvitationDelivery
{
    /// <summary>True only when real SMTP delivery and a valid <c>Identity:PublicUrl</c> are both configured.</summary>
    bool Available { get; }

    /// <summary>
    /// Delivers an invitation link built solely from <c>Identity:PublicUrl</c> through real SMTP
    /// without recording the token, link, body, or transport error.
    /// </summary>
    Task<bool> TrySendAsync(string address, string displayName, string username, string token, CancellationToken ct);
}
