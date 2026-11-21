using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Models.Ui;

/// <summary>What the Login page offers besides the password: the identity provider's button.</summary>
public sealed record AuthOptionsResponse(bool SsoEnabled, string SsoDisplayName);

public sealed record PasswordSignInRequest(
    [Required, StringLength(256)] string Username,
    [Required, StringLength(256)] string Password,
    string? ReturnUrl);

/// <summary>The second-factor page the pending sign-in goes to next.</summary>
public sealed record PasswordStageResponse([property: AllowedValues("totp", "enroll")] string Next);

/// <summary>An authenticator code: the TOTP step or the confirmation of a first enrollment.</summary>
public sealed record AuthenticatorCodeRequest(
    [Required, RegularExpression(@"^\d{6}$")] string Code,
    string? ReturnUrl);

public sealed record RecoveryCodeSignInRequest(
    [Required, RegularExpression(@"^[A-Za-z0-9\- ]{10,16}$")] string Code,
    string? ReturnUrl);

/// <summary>Where a finished sign-in lands, always a target the server has judged.</summary>
public sealed record SignInRedirectResponse(string Redirect);

public sealed record RecoveryCodeSignInResponse(string Redirect, int CodesRemaining);

/// <summary>The authenticator to enroll, as text and as a QR code PNG data URI.</summary>
public sealed record TotpEnrollmentResponse(string SecretBase32, string QrCodeDataUri);

public sealed record PasskeySignInBeginRequest(string? ReturnUrl);

public sealed record PasskeySignInCompleteRequest(JsonElement Credential);

public sealed record RecoverRequest(
    [Required, StringLength(64)] string Username,
    [Required, StringLength(256)] string Password,
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit code from your authenticator app.")] string Code);

public sealed record RecoverResponse(bool BlockLifted);

/// <summary>A refused sign-in call. A wrong credential is a 400, never a 401, which means the pending sign-in is gone.</summary>
public sealed class SignInProblemResponse : ProblemDetails
{
    public required string Code { get; init; }
}
