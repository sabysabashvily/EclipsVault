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
    private const int CeremonyTimeoutMs = 120_000;

    private readonly IUserRepository _users;
    private readonly IPasskeyCredentialRepository _passkeys;
    private readonly IAccountChangeCommitter _changes;
    private readonly IAuthenticationAttempt _attempt;
    private readonly AuthenticationFailures _failures;
    private readonly WebAuthnOptions _options;
    private readonly TimeProvider _clock;

    public PasskeyService(
        IUserRepository users,
        IPasskeyCredentialRepository passkeys,
        IAccountChangeCommitter changes,
        IAuthenticationAttempt attempt,
        AuthenticationFailures failures,
        IOptions<WebAuthnOptions> options,
        TimeProvider clock)
    {
        _users = users;
        _passkeys = passkeys;
        _changes = changes;
        _attempt = attempt;
        _failures = failures;
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
                new { type = "public-key", alg = -7 },
                new { type = "public-key", alg = -257 }
            },
            timeout = CeremonyTimeoutMs,
            excludeCredentials = existing
                .Select(p => new { type = "public-key", id = Base64Url.EncodeToString(p.CredentialId) })
                .ToArray(),
            authenticatorSelection = new { residentKey = "preferred", userVerification = "required" },
            attestation = "none"
        };

        return new PasskeyCeremonyOptions(JsonSerializer.Serialize(options), Base64Url.EncodeToString(challenge));
    }

    public async Task<PasskeyRegistrationResult> CompleteRegistrationAsync(
        Guid userId, string expectedChallenge, string responseJson, string? nickname, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        if (user is null)
        {
            return PasskeyRegistrationResult.Failed("Your account could not be loaded.");
        }

        byte[] rawId, clientDataJson, attestationObject;
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            rawId = DecodeB64Url(root, "id");
            clientDataJson = DecodeB64Url(root, "clientDataJSON");
            attestationObject = DecodeB64Url(root, "attestationObject");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException)
        {
            return PasskeyRegistrationResult.Failed("The passkey response was malformed.");
        }

        if (VerifyClientData(clientDataJson, "webauthn.create", expectedChallenge) is { } clientError)
        {
            return PasskeyRegistrationResult.Failed(clientError);
        }

        AttestedCredential credential;
        try
        {
            credential = WebAuthnVerifier.ParseAttestation(attestationObject, _options.RelyingPartyId, requireUserVerification: true);
        }
        catch (WebAuthnException ex)
        {
            return PasskeyRegistrationResult.Failed(ex.Message);
        }

        if (!credential.CredentialId.AsSpan().SequenceEqual(rawId))
        {
            return PasskeyRegistrationResult.Failed("The credential id did not match the attestation.");
        }

        if (await _passkeys.FindByCredentialIdAsync(credential.CredentialId, ct) is not null)
        {
            return PasskeyRegistrationResult.Failed("That passkey is already registered.");
        }

        var entity = new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CredentialId = credential.CredentialId,
            PublicKey = credential.CosePublicKey,
            SignCount = credential.SignCount,
            Nickname = NormalizeNickname(nickname),
            CreatedAtUtc = _clock.GetUtcNow()
        };

        _passkeys.StageAddition(entity);
        await _changes.CommitAsync(user, AuditAction.PasskeyRegistered, $"Passkey '{entity.Nickname}' registered", ct);
        return PasskeyRegistrationResult.Ok;
    }

    public async Task<PasskeyCeremonyOptions> BeginAssertionAsync(string? usernameOrEmail, CancellationToken ct)
    {
        var challenge = RandomNumberGenerator.GetBytes(ChallengeBytes);

        // A named user scopes the allowed credentials; an unknown or omitted name yields an
        // empty list (discoverable-credential mode), which also avoids leaking account existence.
        IReadOnlyList<byte[]> allowed = [];
        if (!string.IsNullOrWhiteSpace(usernameOrEmail))
        {
            var user = await _users.FindByUsernameOrEmailAsync(usernameOrEmail.Trim(), ct);
            if (user is not null)
            {
                var creds = await _passkeys.ListForUserAsync(user.Id, ct);
                allowed = creds.Select(c => c.CredentialId).ToList();
            }
        }

        var options = new
        {
            challenge = Base64Url.EncodeToString(challenge),
            timeout = CeremonyTimeoutMs,
            rpId = _options.RelyingPartyId,
            allowCredentials = allowed
                .Select(id => new { type = "public-key", id = Base64Url.EncodeToString(id) })
                .ToArray(),
            userVerification = "required"
        };

        return new PasskeyCeremonyOptions(JsonSerializer.Serialize(options), Base64Url.EncodeToString(challenge));
    }

    public async Task<PasskeyAssertionResult> CompleteAssertionAsync(string expectedChallenge, string responseJson, CancellationToken ct)
    {
        byte[] rawId, clientDataJson, authenticatorData, signature;
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            rawId = DecodeB64Url(root, "id");
            clientDataJson = DecodeB64Url(root, "clientDataJSON");
            authenticatorData = DecodeB64Url(root, "authenticatorData");
            signature = DecodeB64Url(root, "signature");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException)
        {
            return PasskeyAssertionResult.Failed("The passkey response was malformed.");
        }

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

        return await _attempt.RunAsync(user, async token =>
        {
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

            uint newSignCount;
            try
            {
                newSignCount = WebAuthnVerifier.VerifyAssertion(
                    authenticatorData, clientDataJson, signature, stored.PublicKey,
                    _options.RelyingPartyId, requireUserVerification: true);
            }
            catch (WebAuthnException)
            {
                await _failures.RecordAsync(user, AuditAction.LoginFailed, "Passkey signature verification failed", token);
                return PasskeyAssertionResult.Failed("This passkey could not be verified.");
            }

            // A counter that fails to advance suggests a cloned authenticator. Zero-only
            // authenticators do not provide this signal and remain usable.
            if ((newSignCount != 0 || stored.SignCount != 0) && newSignCount <= (uint)stored.SignCount)
            {
                await _failures.RecordAsync(user, AuditAction.LoginFailed, "Passkey signature counter did not advance (possible clone)", token);
                return PasskeyAssertionResult.Failed("This passkey could not be verified.");
            }

            if (!await _passkeys.TryAdvanceSignCountAsync(stored, newSignCount, token))
            {
                await _failures.RecordAsync(user, AuditAction.LoginFailed, "Passkey sign-in rejected: credential changed", token);
                return PasskeyAssertionResult.Failed("This passkey could not be verified.");
            }

            await _failures.ResetAsync(user, token);
            _attempt.RecordUserEvent(AuditAction.PasskeyLogin, user, "Passwordless sign-in with a passkey");
            return PasskeyAssertionResult.Succeeded(Map(user));
        }, ct);
    }

    public async Task<IReadOnlyList<PasskeySummary>> ListForUserAsync(Guid userId, CancellationToken ct)
    {
        var creds = await _passkeys.ListForUserAsync(userId, ct);
        return creds
            .Select(c => new PasskeySummary(c.Id, string.IsNullOrWhiteSpace(c.Nickname) ? "Passkey" : c.Nickname!, c.CreatedAtUtc))
            .ToList();
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid passkeyId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId, ct);
        var credential = await _passkeys.FindByIdForUserAsync(passkeyId, userId, ct);
        if (user is null || credential is null)
        {
            return false;
        }

        _passkeys.StageRemoval(credential);
        return await _changes.CommitAsync(user, AuditAction.PasskeyRemoved, $"Passkey '{credential.Nickname ?? "Passkey"}' removed", ct);
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

    private static string NormalizeNickname(string? nickname)
    {
        nickname = nickname?.Trim();
        if (string.IsNullOrEmpty(nickname))
        {
            return "Passkey";
        }

        return nickname.Length > 64 ? nickname[..64] : nickname;
    }

    private static UserDto Map(User user)
        => new(user.Id, user.Username, user.DisplayName, user.Email, user.Clearance, user.ProjectKey, user.TotpEnabled, user.IsDisabled);
}
