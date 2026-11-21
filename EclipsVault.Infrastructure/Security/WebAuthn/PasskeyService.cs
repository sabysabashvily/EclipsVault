using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using Microsoft.Extensions.Options;

namespace EclipsVault.Infrastructure.Security.WebAuthn;

/// <summary>
/// Orchestrates the WebAuthn ceremonies: builds the option payloads, verifies responses via
/// <see cref="WebAuthnVerifier"/>, and persists credentials. Adding and removing a passkey commit
/// together with their audit event. Sign-in events commit with their counter and lockout bookkeeping
/// through <see cref="IAuthenticationAttempt"/>. A registered passkey with user verification is a
/// self-contained second factor, so a successful assertion signs the user in without a
/// password or TOTP — passwordless MFA.
/// </summary>
public sealed class PasskeyService : IPasskeyService
{
    private const int ChallengeBytes = 32;
    private const int Es256Algorithm = -7;
    private const int Rs256Algorithm = -257;
    private const int CeremonyTimeoutMs = 120_000;

    private readonly IUserRepository _users;
    private readonly IPasskeyCredentialRepository _passkeys;
    private readonly IAuthenticationAttempt _attempt;
    private readonly AuthenticationFailures _failures;
    private readonly ISessionRevocationService _revocation;
    private readonly WebAuthnOptions _options;
    private readonly TimeProvider _clock;

    public PasskeyService(
        IUserRepository users,
        IPasskeyCredentialRepository passkeys,
        IAuthenticationAttempt attempt,
        AuthenticationFailures failures,
        ISessionRevocationService revocation,
        IOptions<WebAuthnOptions> options,
        TimeProvider clock)
    {
        _users = users;
        _passkeys = passkeys;
        _attempt = attempt;
        _failures = failures;
        _revocation = revocation;
        _options = options.Value;
        _clock = clock;
    }

    public async Task<PasskeyCeremonyOptions> BeginRegistrationAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct)
                   ?? throw new InvalidOperationException("The signed-in user could not be loaded.");

        var existing = await _passkeys.ListForUserAsync(userId, ct);
        var challenge = RandomNumberGenerator.GetBytes(ChallengeBytes);

        var options = new
        {
            rp = new { id = _options.RelyingPartyId, name = _options.RelyingPartyName },
            user = new
            {
                id = Base64Url.EncodeToString(user.Id.ToByteArray()),
                name = user.Username,
                displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName
            },
            challenge = Base64Url.EncodeToString(challenge),
            pubKeyCredParams = new[]
            {
                new { type = "public-key", alg = Es256Algorithm },
                new { type = "public-key", alg = Rs256Algorithm }
            },
            timeout = CeremonyTimeoutMs,
            excludeCredentials = existing
                .Select(p => new { type = "public-key", id = Base64Url.EncodeToString(p.CredentialId) })
                .ToArray(),
            authenticatorSelection = new { residentKey = "required", userVerification = "required" },
            attestation = "none"
        };

        return new PasskeyCeremonyOptions(JsonSerializer.Serialize(options), Base64Url.EncodeToString(challenge));
    }

    public async Task<PasskeyAttestation> VerifyRegistrationAsync(
        Guid userId, string expectedChallenge, string responseJson, string nickname, CancellationToken ct)
    {
        byte[] rawId, clientDataJson, attestationObject;
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            rawId = DecodeB64Url(root, "id");
            clientDataJson = DecodeB64Url(root, "clientDataJSON");
            attestationObject = DecodeB64Url(root, "attestationObject");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return PasskeyAttestation.Failed("The passkey response was malformed.");
        }

        if (VerifyClientData(clientDataJson, "webauthn.create", expectedChallenge) is { } clientError)
        {
            return PasskeyAttestation.Failed(clientError);
        }

        AttestedCredential credential;
        try
        {
            credential = WebAuthnVerifier.ParseAttestation(attestationObject, _options.RelyingPartyId, requireUserVerification: true);
        }
        catch (WebAuthnException ex)
        {
            return PasskeyAttestation.Failed(ex.Message);
        }

        if (!credential.CredentialId.AsSpan().SequenceEqual(rawId))
        {
            return PasskeyAttestation.Failed("The credential id did not match the attestation.");
        }

        if (await _passkeys.FindByCredentialIdAsync(credential.CredentialId, ct) is not null)
        {
            return PasskeyAttestation.Failed("That passkey is already registered.");
        }

        return new PasskeyAttestation(new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CredentialId = credential.CredentialId,
            PublicKey = credential.CosePublicKey,
            SignCount = credential.SignCount,
            Nickname = nickname,
            CreatedAtUtc = _clock.GetUtcNow()
        }, null);
    }

    public async Task<PasskeyCeremonyOptions> BeginAccountVerificationAsync(Guid userId, Guid? excludedPasskeyId, CancellationToken ct)
    {
        var challenge = RandomNumberGenerator.GetBytes(ChallengeBytes);
        var allowed = (await _passkeys.ListForUserAsync(userId, ct)).Where(key => key.Id != excludedPasskeyId);
        var options = new
        {
            challenge = Base64Url.EncodeToString(challenge),
            timeout = CeremonyTimeoutMs,
            rpId = _options.RelyingPartyId,
            allowCredentials = allowed
                .Select(key => new { type = "public-key", id = Base64Url.EncodeToString(key.CredentialId) })
                .ToArray(),
            userVerification = "required"
        };

        return new PasskeyCeremonyOptions(JsonSerializer.Serialize(options), Base64Url.EncodeToString(challenge));
    }

    public async Task<PasskeyProof> VerifyAccountAssertionAsync(
        User account, string expectedChallenge, string responseJson, Guid? excludedPasskeyId, CancellationToken ct)
    {
        if (ReadAssertion(responseJson) is not { } response)
        {
            return PasskeyProof.Malformed;
        }

        var (rawId, clientDataJson, authenticatorData, signature) = response;

        if (VerifyClientData(clientDataJson, "webauthn.get", expectedChallenge) is { } clientError)
        {
            return PasskeyProof.Rejected($"Passkey verification rejected: {clientError}");
        }

        // Another account's valid passkey proves that account, not this one.
        var stored = await _passkeys.FindByCredentialIdAsync(rawId, ct);
        if (stored is null || stored.UserId != account.Id || stored.Id == excludedPasskeyId)
        {
            return PasskeyProof.Rejected("Passkey verification rejected: not one of this account's usable passkeys");
        }

        if (!await _passkeys.RefreshAsync(stored, ct))
        {
            return PasskeyProof.Rejected("Passkey verification rejected: credential was removed");
        }

        return await VerifyAssertionAndAdvanceCounterAsync(stored, authenticatorData, clientDataJson, signature, ct) is { } rejection
            ? PasskeyProof.Rejected(rejection)
            : PasskeyProof.Verified;
    }

    public Task<PasskeyCeremonyOptions> BeginAssertionAsync(CancellationToken ct)
    {
        var challenge = RandomNumberGenerator.GetBytes(ChallengeBytes);
        var options = new
        {
            challenge = Base64Url.EncodeToString(challenge),
            timeout = CeremonyTimeoutMs,
            rpId = _options.RelyingPartyId,
            allowCredentials = Array.Empty<object>(),
            userVerification = "required"
        };

        return Task.FromResult(new PasskeyCeremonyOptions(JsonSerializer.Serialize(options), Base64Url.EncodeToString(challenge)));
    }

    public async Task<PasskeyAssertionResult> CompleteAssertionAsync(string expectedChallenge, string responseJson, CancellationToken ct)
    {
        if (ReadAssertion(responseJson) is not { } response)
        {
            return PasskeyAssertionResult.Failed("The passkey response was malformed.");
        }

        var (rawId, clientDataJson, authenticatorData, signature) = response;

        if (VerifyClientData(clientDataJson, "webauthn.get", expectedChallenge) is { } clientError)
        {
            return PasskeyAssertionResult.Failed(clientError);
        }

        var stored = await _passkeys.FindByCredentialIdAsync(rawId, ct);
        if (stored is null)
        {
            return PasskeyAssertionResult.Failed("This passkey is not recognized.");
        }

        var user = await _users.FindByIdAsync(stored.UserId, ct);
        if (user is null)
        {
            return PasskeyAssertionResult.Failed("The account for this passkey no longer exists.");
        }

        // The credential has just identified its account, so its generation is taken now, before the
        // assertion is verified. Both are captured before the attempt reloads the account, so a change
        // committed since then refuses this sign-in instead of passing it under a newer stamp.
        var credentialGeneration = user.CredentialGeneration;
        var revocationVersion = await _revocation.CaptureAsync(user.Id, ct);
        return await _attempt.RunAsync(user, async token =>
        {
            if (user.CredentialGeneration != credentialGeneration
                || await _revocation.FindAsync(user.Id, token) != revocationVersion)
            {
                return PasskeyAssertionResult.Failed("This sign-in expired. Try again.");
            }

            if (user.AwaitingActivation)
            {
                _attempt.RecordUserEvent(AuditAction.LoginFailed, user, "Passkey sign-in blocked: account is awaiting activation");
                return PasskeyAssertionResult.Failed("This passkey could not be verified.");
            }

            if (user.IsDisabled)
            {
                _attempt.RecordUserEvent(AuditAction.LoginFailed, user, "Passkey sign-in blocked: account is disabled");
                return PasskeyAssertionResult.Failed("This passkey could not be verified.");
            }

            if (user.IsLockedOut(_clock.GetUtcNow()))
            {
                _attempt.RecordUserEvent(AuditAction.LoginFailed, user, "Passkey sign-in blocked: account is locked out");
                return PasskeyAssertionResult.Failed("This passkey could not be verified.");
            }

            // Another assertion can advance the counter while this request waits for the account
            // lock. Compare against that committed value, not the copy read before waiting.
            if (!await _passkeys.RefreshAsync(stored, token))
            {
                _attempt.RecordUserEvent(AuditAction.LoginFailed, user, "Passkey sign-in rejected: credential was removed");
                return PasskeyAssertionResult.Failed("This passkey could not be verified.");
            }

            if (await VerifyAssertionAndAdvanceCounterAsync(stored, authenticatorData, clientDataJson, signature, token) is { } rejection)
            {
                await _failures.RecordAsync(user, AuditAction.LoginFailed, rejection, token);
                return PasskeyAssertionResult.Failed("This passkey could not be verified.");
            }

            await _failures.ResetAsync(user, token);
            // The sign-in itself is recorded when the session is issued.
            _attempt.RecordUserEvent(AuditAction.SignInFactorVerified, user, "Passkey assertion accepted");
            return PasskeyAssertionResult.Succeeded(Map(user), new SessionStamp(credentialGeneration, revocationVersion));
        }, ct);
    }

    public async Task<IReadOnlyList<PasskeySummary>> ListForUserAsync(Guid userId, CancellationToken ct)
    {
        var creds = await _passkeys.ListForUserAsync(userId, ct);
        return creds
            .Select(c => new PasskeySummary(c.Id, string.IsNullOrWhiteSpace(c.Nickname) ? "Passkey" : c.Nickname!, c.CreatedAtUtc))
            .ToList();
    }

    /// <summary>
    /// Verifies the assertion's signature with user verification and advances the stored counter.
    /// Returns why the assertion was refused, or null when it proved the passkey.
    /// </summary>
    private async Task<string?> VerifyAssertionAndAdvanceCounterAsync(
        PasskeyCredential stored, byte[] authenticatorData, byte[] clientDataJson, byte[] signature, CancellationToken ct)
    {
        uint newSignCount;
        try
        {
            newSignCount = WebAuthnVerifier.VerifyAssertion(
                authenticatorData, clientDataJson, signature, stored.PublicKey,
                _options.RelyingPartyId, requireUserVerification: true);
        }
        catch (WebAuthnException)
        {
            return "Passkey signature verification failed";
        }

        // A counter that fails to advance suggests a cloned authenticator. Zero-only
        // authenticators do not provide this signal and remain usable.
        if ((newSignCount != 0 || stored.SignCount != 0) && newSignCount <= (uint)stored.SignCount)
        {
            return "Passkey signature counter did not advance (possible clone)";
        }

        return await _passkeys.TryAdvanceSignCountAsync(stored, newSignCount, ct)
            ? null
            : "Passkey rejected: credential changed";
    }

    /// <summary>Validates ceremony type, challenge, and origin from the raw clientDataJSON. Returns an error message, or null when valid.</summary>
    private string? VerifyClientData(byte[] clientDataJson, string expectedType, string expectedChallenge)
    {
        try
        {
            using var doc = JsonDocument.Parse(clientDataJson);
            var root = doc.RootElement;

            if (root.GetProperty("type").GetString() != expectedType)
            {
                return "Unexpected passkey ceremony type.";
            }

            if (!FixedTimeEquals(root.GetProperty("challenge").GetString(), expectedChallenge))
            {
                return "The passkey challenge did not match.";
            }

            var origin = root.GetProperty("origin").GetString();
            if (origin is null || !_options.Origins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                return "The passkey origin is not allowed.";
            }

            return null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "The passkey client data was malformed.";
        }
    }

    private static AssertionResponse? ReadAssertion(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new AssertionResponse(DecodeB64Url(root, "id"), DecodeB64Url(root, "clientDataJSON"),
                DecodeB64Url(root, "authenticatorData"), DecodeB64Url(root, "signature"));
        }
        catch (Exception failure) when (failure is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record AssertionResponse(byte[] Id, byte[] ClientData, byte[] AuthenticatorData, byte[] Signature);

    private static byte[] DecodeB64Url(JsonElement root, string property)
    {
        var value = root.GetProperty(property).GetString()
                    ?? throw new FormatException($"'{property}' was null.");
        return Base64Url.DecodeFromChars(value);
    }

    private static bool FixedTimeEquals(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }

    private static UserDto Map(User user)
        => new(user.Id, user.Username, user.DisplayName, user.Email, user.Clearance, user.ProjectKey, user.TotpEnabled, user.IsDisabled);
}
