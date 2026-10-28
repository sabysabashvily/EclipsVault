using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// The Profile page. Every field is chosen here: credential generations, revocation versions,
/// password, authenticator and recovery secrets and passkey public keys never reach the browser.
/// </summary>
/// <param name="Clearance">The clearance's display name, e.g. "Top Secret".</param>
/// <param name="AvatarUrl">The account's avatar, from the existing avatar endpoint.</param>
/// <param name="VerificationMethods">The methods that can verify a security change of this account.</param>
public sealed record ProfileResponse(
    string Username,
    string DisplayName,
    string Email,
    string Clearance,
    string Project,
    string AvatarUrl,
    bool AuthenticatorEnrolled,
    int RecoveryCodesRemaining,
    IReadOnlyList<ProfilePasskeyResponse> Passkeys,
    [property: UiEnum(typeof(AccountProofMethod))] IReadOnlyList<string> VerificationMethods,
    ProfileChangesResponse Changes,
    PendingEmailResponse? PendingEmail);
