using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Users;

/// <summary>The invited account and the authenticator seed its activation enrolls.</summary>
public sealed record ActivationInvitationDto(
    string Username, string DisplayName, string SecretBase32, string OtpAuthUri, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Account activation by invitation (ADR-0033), for the person holding the emailed token. Nothing here
/// tells the caller why a token is not usable.
/// </summary>
public interface IAccountActivationService
{
    /// <summary>
    /// Shows the invited account and its pending authenticator seed, generating the seed on the first
    /// inspection only. Spends nothing and records nothing.
    /// </summary>
    /// <returns>Null when the token is unknown, used, expired or replaced, or its account is disabled.</returns>
    Task<ActivationInvitationDto?> InspectAsync(string token, CancellationToken ct);

    /// <summary>
    /// Sets the password, enrolls the inspected seed confirmed by <paramref name="code"/>, spends the token,
    /// advances the credential generation and records <c>AccountActivated</c>, all in one transaction.
    /// </summary>
    /// <exception cref="AccountInputException">The password breaks Profile's rules; nothing changed.</exception>
    /// <exception cref="AuditWriteFailedException">The activation and its event could not be stored; neither was.</exception>
    Task<ActivationOutcome> CompleteAsync(string token, string password, string code, CancellationToken ct);
}
