using System.Diagnostics;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// The Profile UI API's refusals as Problem Details with stable codes (ADR-0006). A wrong proof is a
/// 400 the page handles itself, because 401 is reserved for an unusable session and makes the
/// Angular UI sign out. An audit failure is left to the global 503 <c>audit_unavailable</c>.
/// </summary>
public static class ProfileProblems
{
    public static JsonResult Problem(int status, string code, string title, IReadOnlyDictionary<string, string[]>? errors = null)
        => new(new ProfileProblemResponse { Status = status, Code = code, Title = title, Errors = errors })
        {
            StatusCode = status,
            ContentType = "application/problem+json"
        };

    public static JsonResult InvalidField(string field, string message)
        => Problem(StatusCodes.Status400BadRequest, "validation_failed", message, new Dictionary<string, string[]> { [field] = [message] });

    public static JsonResult ChangedSignInRequired()
        => Problem(StatusCodes.Status503ServiceUnavailable, "changed_sign_in_required",
            "The change was saved, but this browser could not stay signed in. Sign in again to continue.");

    public static JsonResult For(ProfileOperationRefusedException refused) => refused.Refusal switch
    {
        ProfileRefusal.MailUnavailable => Conflict("mail_unavailable", "Email changes require configured, enabled SMTP. Delivery may be unavailable; verify again before deliberately resending."),
        ProfileRefusal.EmailRequestIncomplete => Problem(StatusCodes.Status503ServiceUnavailable, "email_request_incomplete",
            "The code was sent, but the request could not be completed. Your current email is unchanged. Verify again before deliberately resending."),
        ProfileRefusal.EmailConflict => Conflict("email_conflict", "That email address cannot be used."),
        ProfileRefusal.EmailCodeInvalid => Problem(StatusCodes.Status400BadRequest, "email_code_invalid", "That mailbox code is not valid."),
        ProfileRefusal.EmailCodeExpired => Conflict("email_code_expired", "The mailbox code expired. Start the change again."),
        ProfileRefusal.InvalidInput => InvalidField(refused.Field ?? "request", refused.Message),
        ProfileRefusal.VerificationFailed => Problem(StatusCodes.Status400BadRequest, "verification_failed",
            "That didn't verify your account. Check what you entered and try again."),
        ProfileRefusal.Throttled => Problem(StatusCodes.Status429TooManyRequests, "throttled",
            "Too many attempts. Wait a few minutes and try again."),
        ProfileRefusal.OperationNotFound => Problem(StatusCodes.Status404NotFound, "operation_not_found",
            "That change no longer exists. Start it again."),
        ProfileRefusal.PasskeyNotFound => Problem(StatusCodes.Status404NotFound, "passkey_not_found",
            "That passkey could not be found."),
        ProfileRefusal.OperationExpired => Conflict("operation_expired", "This change was not verified in time. Start it again."),
        ProfileRefusal.OperationChanged => Conflict("operation_changed", "Your account changed since this change was started. Start it again."),
        ProfileRefusal.CeremonyExpired => Conflict("ceremony_expired", "The passkey prompt expired. Try again."),
        ProfileRefusal.VerificationRequired => Conflict("verification_required", "Verify your account to make this change."),
        ProfileRefusal.VerificationExpired => Conflict("verification_expired", "Your verification expired. Verify your account again."),
        ProfileRefusal.MethodUnavailable => Conflict("method_unavailable", "This account can't be verified that way."),
        ProfileRefusal.AlternateMethodRequired => Conflict("alternate_method_required",
            "Removing your last passkey needs another way to sign in that keeps working afterwards."),
        ProfileRefusal.AuthenticatorNotEnrolled => Conflict("authenticator_not_enrolled", "This needs an authenticator app set up first."),
        _ => throw new UnreachableException($"No Problem Details for {refused.Refusal}."),
    };

    /// <summary>The code the Profile page shows for a change that cannot be started, or null when it can.</summary>
    public static string? CodeOf(ProfileRefusal? refusal) => refusal switch
    {
        null => null,
        ProfileRefusal.AuthenticatorNotEnrolled => "authenticator_not_enrolled",
        ProfileRefusal.MailUnavailable => "mail_unavailable",
        ProfileRefusal.AlternateMethodRequired => "alternate_method_required",
        _ => "method_unavailable",
    };

    private static JsonResult Conflict(string code, string title) => Problem(StatusCodes.Status409Conflict, code, title);
}

/// <summary>Answers the Profile controllers' domain refusals with their Problem Details.</summary>
public sealed class ProfileProblemFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            ProfileOperationRefusedException refused => ProfileProblems.For(refused),
            EmailAddressTakenException => ProfileProblems.For(new ProfileOperationRefusedException(ProfileRefusal.EmailConflict)),
            ProfileException invalid => ProfileProblems.Problem(StatusCodes.Status400BadRequest, "validation_failed", invalid.Message),
            AccountChangedException or StaleProofException => ProfileProblems.For(new ProfileOperationRefusedException(ProfileRefusal.OperationChanged)),
            _ => null,
        };
        context.ExceptionHandled = context.Result is not null;
    }
}
