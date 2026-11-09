using System.ComponentModel.DataAnnotations;

namespace EclipsVault.Web.Models.Ui;

public sealed record NotificationsResponse(
    [property: AllowedValues("off", "smtp", "log")] string Delivery,
    string? SmtpTarget, IReadOnlyList<NotificationResponse> Outbox);

public sealed record NotificationResponse(DateTimeOffset SentAtUtc, string To, string Subject, string Body,
    string EventType, string Transport,
    [property: AllowedValues("sent", "failed", "suppressed")] string Status, string? Error);
