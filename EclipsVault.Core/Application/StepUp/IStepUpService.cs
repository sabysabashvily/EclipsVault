using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.StepUp;

/// <summary>Decides when a fresh re-authentication is required and verifies it.</summary>
public interface IStepUpService
{
    /// <summary>The configured freshness window, in minutes (for UI messaging).</summary>
    int MaxAuthAgeMinutes { get; }

    /// <summary>
    /// True when revealing a secret of this sensitivity requires a fresh re-authentication —
    /// i.e. it is at or above the configured threshold and the last strong auth is too old.
    /// Pure and deterministic.
    /// </summary>
    bool IsRequired(SensitivityLevel sensitivity, DateTimeOffset lastStrongAuthUtc, DateTimeOffset nowUtc);

    /// <summary>
    /// Validates the user's current authenticator code for a step-up of a session issued under
    /// <paramref name="credentialGeneration"/>. Audited. Returns true on success.
    /// </summary>
    /// <exception cref="Domain.Exceptions.StaleProofException">The account's credentials changed after the session was issued.</exception>
    Task<bool> VerifyAsync(Guid userId, SessionStamp stamp, string code, CancellationToken ct);
}
