using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

[ApiController]
[Route("api/ui/notifications")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.AdminOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(INotificationService notifications, IEmailTransportStatus transport) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<NotificationsResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<NotificationsResponse>> Get(CancellationToken ct)
        => new NotificationsResponse(!transport.Enabled ? "off"
                : string.Equals(transport.Transport, "Smtp", StringComparison.OrdinalIgnoreCase) ? "smtp" : "log",
            transport.SmtpTarget,
            (await notifications.ListRecentAsync(200, ct)).Select(email => new NotificationResponse(
                email.CreatedAtUtc, email.ToAddress, email.Subject, email.Body, email.EventType, email.Transport,
                email.Status switch
                {
                    EmailDeliveryStatus.Sent => "sent",
                    EmailDeliveryStatus.Failed => "failed",
                    _ => "suppressed"
                }, email.Error)).ToList());
}
