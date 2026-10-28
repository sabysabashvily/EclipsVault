using Microsoft.Extensions.Options;

namespace EclipsVault.Infrastructure.Notifications;

public sealed class EmailVerificationDelivery(SmtpEmailSender smtp, IOptions<EmailOptions> options) : IEmailVerificationDelivery
{
    public bool Available => options.Value.Enabled
        && string.Equals(options.Value.Sender, "Smtp", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(options.Value.Smtp.Host)
        && options.Value.Smtp.Port is > 0 and <= 65535
        && System.Net.Mail.MailAddress.TryCreate(options.Value.FromAddress, out _);

    public async Task<bool> TrySendAsync(string address, string code, CancellationToken ct)
    {
        if (!Available)
        {
            return false;
        }

        try
        {
            await smtp.SendAsync(new EmailMessage(address, "Confirm your EclipsVault email",
                $"Enter this code on the Profile page where you requested the change: {code}\n\nIt expires in 15 minutes. No address changes until you confirm there."), ct);
            return true;
        }
        catch
        {
            // Relay replies can repeat message content. Neither the exception nor its text is safe evidence.
            return false;
        }
    }
}
