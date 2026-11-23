using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace EclipsVault.Infrastructure.Notifications;

public sealed class AccountInvitationDelivery(
    SmtpEmailSender smtp,
    IOptions<EmailOptions> options,
    UserDirectoryOptions directory) : IAccountInvitationDelivery
{
    private const string Subject = "You're invited to EclipsVault";

    public bool Available => IsRealSmtpConfigured(options.Value)
        && InvitationLink.TryParsePublicUrl(directory.PublicUrl, out _);

    public async Task<bool> TrySendAsync(string address, string displayName, string username, string token, CancellationToken ct)
    {
        if (!IsRealSmtpConfigured(options.Value)
            || !InvitationLink.TryParsePublicUrl(directory.PublicUrl, out var publicUrl))
        {
            return false;
        }

        var link = InvitationLink.Build(publicUrl, token);
        var name = string.IsNullOrWhiteSpace(displayName) ? username : displayName;
        var body = $"Hello {name}, an administrator created an EclipsVault account for you with the username '{username}'. " +
                   $"Open this link within 72 hours to choose your password and set up your authenticator: {link}. " +
                   "If you weren't expecting this, ignore this email.";

        try
        {
            await smtp.SendAsync(new EmailMessage(address, Subject, body), ct);
            return true;
        }
        catch
        {
            // Relay replies can echo message content including the invitation link. Never log or rethrow them.
            return false;
        }
    }

    private static bool IsRealSmtpConfigured(EmailOptions email)
        => email.Enabled
            && string.Equals(email.Sender, "Smtp", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(email.Smtp.Host)
            && email.Smtp.Port is > 0 and <= 65535
            && MailAddress.TryCreate(email.FromAddress, out _);
}
