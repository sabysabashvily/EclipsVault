using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Notifications;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Controllers.Ui;
using Xunit;

namespace EclipsVault.Tests.Web;

public sealed class NotificationsResponseTests
{
    [Theory]
    [InlineData(false, "Smtp", "off")]
    [InlineData(false, "Log", "off")]
    [InlineData(true, "Smtp", "smtp")]
    [InlineData(true, "sMtP", "smtp")]
    [InlineData(true, "Log", "log")]
    public async Task Delivery_reports_whether_the_configured_transport_can_send(bool enabled, string transport, string expected)
    {
        var controller = new NotificationsController(new Outbox([]), new TransportStatus(enabled, transport, "mail.example.test:2525"));

        var response = (await controller.Get(CancellationToken.None)).Value;

        Assert.Equal(expected, response!.Delivery);
        Assert.Equal("mail.example.test:2525", response.SmtpTarget);
        Assert.Empty(response.Outbox);
    }

    [Theory]
    [InlineData(EmailDeliveryStatus.Sent, "sent")]
    [InlineData(EmailDeliveryStatus.Failed, "failed")]
    [InlineData(EmailDeliveryStatus.Suppressed, "suppressed")]
    public async Task Each_delivery_outcome_preserves_the_composed_email(EmailDeliveryStatus status, string expected)
    {
        var sentAt = DateTimeOffset.Parse("2026-01-02T03:04:00Z");
        var outbox = new Outbox([new(Guid.NewGuid(), "reader@example.test", "A secret expires", "FINANCE_DECOY_KEY expires soon",
            "SecretExpiring", "Smtp", status, "Delivery refused", sentAt)]);

        var response = (await new NotificationsController(outbox, new TransportStatus(true, "Smtp", "mail:25")).Get(CancellationToken.None)).Value;

        var email = Assert.Single(response!.Outbox);
        Assert.Equal(new EclipsVault.Web.Models.Ui.NotificationResponse(sentAt, "reader@example.test", "A secret expires",
            "FINANCE_DECOY_KEY expires soon", "SecretExpiring", "Smtp", expected, "Delivery refused"), email);
        Assert.Equal(200, outbox.RequestedLimit);
    }

    private sealed record TransportStatus(bool Enabled, string TransportName, string? SmtpTarget) : IEmailTransportStatus
    {
        public string Transport => TransportName;
    }

    private sealed class Outbox(IReadOnlyList<EmailLogDto> emails) : INotificationService
    {
        public int RequestedLimit { get; private set; }
        public Task<IReadOnlyList<EmailLogDto>> ListRecentAsync(int max, CancellationToken ct)
        {
            RequestedLimit = max;
            return Task.FromResult(emails);
        }

        public Task NotifyAccessRequestDecidedAsync(Guid requesterUserId, string secretName, bool approved, string reviewer, string? note, CancellationToken ct) => throw new NotSupportedException();
        public Task NotifySensitiveChangeAsync(string trustedAddress, ProfileOperationKind kind, CancellationToken ct) => throw new NotSupportedException();
        public Task NotifyPasswordChangedAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task NotifyCredentialChangedAsync(Guid userId, ProfileOperationKind kind, CancellationToken ct) => throw new NotSupportedException();
        public Task NotifyUserProvisionedAsync(string email, string displayName, string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> NotifyExpiringSecretAsync(Guid ownerUserId, string secretName, DateTimeOffset expiresAtUtc, CancellationToken ct) => throw new NotSupportedException();
    }
}
