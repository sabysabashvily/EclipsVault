using System.ComponentModel.DataAnnotations;

namespace EclipsVault.Web.Models;

public sealed class LoginViewModel
{
    public string? ReturnUrl { get; set; }

    [Required]
    [StringLength(256)]
    [Display(Name = "Username or email")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(256)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Whether an identity provider is configured — the button is absent otherwise.</summary>
    public bool SsoEnabled { get; set; }

    public string SsoDisplayName { get; set; } = "single sign-on";
}

public sealed class TotpViewModel
{
    public string? ReturnUrl { get; set; }

    [Required]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit code from your authenticator app.")]
    [Display(Name = "Authenticator code")]
    public string Code { get; set; } = string.Empty;
}

public sealed class RecoveryCodeViewModel
{
    public string? ReturnUrl { get; set; }

    [Required]
    [RegularExpression(@"^[A-Za-z0-9\- ]{10,16}$", ErrorMessage = "Enter one of your recovery codes.")]
    [Display(Name = "Recovery code")]
    public string Code { get; set; } = string.Empty;
}

public sealed class RecoverViewModel
{
    [Required]
    [StringLength(64)]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(256)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit code from your authenticator app.")]
    [Display(Name = "Authenticator code")]
    public string Code { get; set; } = string.Empty;
}

public sealed class AccessDeniedViewModel
{
    /// <summary>Why the ABAC policy rejected the request; empty when the page is reached without context.</summary>
    public IReadOnlyList<string> Reasons { get; set; } = [];

    /// <summary>The secret that was denied, when the page was reached from a secret access attempt — enables the "Request access" form.</summary>
    public Guid? SecretId { get; set; }
}

public sealed class EnrollTotpViewModel
{
    public string? ReturnUrl { get; set; }

    /// <summary>Display-only; never bound from the request.</summary>
    public string SecretBase32 { get; set; } = string.Empty;

    /// <summary>PNG data URI of the otpauth QR code. Display-only; never bound from the request.</summary>
    public string QrCodeDataUri { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit code from your authenticator app.")]
    [Display(Name = "Confirmation code")]
    public string Code { get; set; } = string.Empty;
}
