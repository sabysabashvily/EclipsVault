using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// Configuration for the HashiCorp Vault Transit crypto engine. Only consulted when
/// <c>Crypto:Engine</c> is set to <see cref="VaultTransitCryptoEngine.EngineName"/>.
/// </summary>
public sealed class VaultOptions
{
    public const string SectionName = "Vault";

    /// <summary>Base address of the Vault server, e.g. <c>https://vault.internal:8200</c>.</summary>
    public string Address { get; set; } = "http://127.0.0.1:8200";

    /// <summary>Mount path of the Transit secrets engine.</summary>
    public string Mount { get; set; } = "transit";

    /// <summary>Name of the Transit key used to wrap data-encryption keys.</summary>
    public string KeyName { get; set; } = "eclipsvault";

    /// <summary>Environment variable holding the Vault token.</summary>
    public string TokenEnvironmentVariable { get; set; } = "VAULT_TOKEN";

    /// <summary>Development-only token used when the environment variable is unset.</summary>
    public string? DevelopmentToken { get; set; }
}

/// <summary>
/// Envelope encryption where the master key lives in <b>HashiCorp Vault</b>, not in this process.
/// The payload is still sealed locally with a single-use AES-256-GCM data-encryption key (DEK),
/// but that DEK is wrapped, unwrapped, and rotated by Vault's Transit engine — so the KEK never
/// exists in application memory, a crash dump, or the database. Selected with
/// <c>Crypto:Engine=VaultTransit</c>; the local <see cref="AesGcmCryptoEngine"/> remains the default.
/// </summary>
public sealed class VaultTransitCryptoEngine : ICryptoEngine
{
    public const string EngineName = "VaultTransit";

    private readonly HttpClient _http;
    private readonly VaultOptions _options;
    private readonly CryptoOptions _crypto;

    public VaultTransitCryptoEngine(HttpClient http, IOptions<VaultOptions> options, IOptions<CryptoOptions> crypto)
    {
        _options = options.Value;
        _crypto = crypto.Value;
        _http = http;
        _http.BaseAddress = new Uri(_options.Address);

        var token = Environment.GetEnvironmentVariable(_options.TokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(token))
        {
            token = _options.DevelopmentToken;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new CryptoConfigurationException(
                $"No Vault token. Set '{_options.TokenEnvironmentVariable}' (or Vault:DevelopmentToken for local dev) to use the Vault Transit engine.");
        }

        _http.DefaultRequestHeaders.Remove("X-Vault-Token");
        _http.DefaultRequestHeaders.Add("X-Vault-Token", token);
    }

    public string EngineId => EngineName;

    public async Task<SealedSecret> SealAsync(byte[] plaintext, byte[] associatedData, CancellationToken ct)
    {
        var dek = RandomNumberGenerator.GetBytes(GcmBlob.DekSize);
        try
        {
            var ciphertext = GcmBlob.Encrypt(dek, plaintext, associatedData);
            // Vault wraps the DEK; the KEK that does it never leaves Vault.
            var wrappedDek = await TransitCallAsync("encrypt", new { plaintext = Convert.ToBase64String(dek) }, "ciphertext", ct);
            return new SealedSecret(
                ciphertext, Encoding.UTF8.GetBytes(wrappedDek), KekId(wrappedDek), SealAlgorithms.AesGcmVaultTransit);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public async Task<byte[]> UnsealAsync(SealedSecret sealedSecret, byte[] associatedData, CancellationToken ct)
    {
        // Materialised to an array rather than kept as the ReadOnlySpan the policy returns: a ref
        // struct cannot live across the await below. This also runs the legacy-blob refusal before we
        // spend a Vault round-trip, so an unbound blob fails fast rather than after a decrypt call.
        byte[] binding = LegacyBlobPolicy.BindingFor(sealedSecret.Algorithm, associatedData, _crypto).ToArray();

        var wrappedDek = Encoding.UTF8.GetString(sealedSecret.WrappedDek); // "vault:v1:…"
        var dekBase64 = await TransitCallAsync("decrypt", new { ciphertext = wrappedDek }, "plaintext", ct);
        var dek = Convert.FromBase64String(dekBase64);
        try
        {
            return GcmBlob.Decrypt(dek, sealedSecret.Ciphertext, binding);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public async Task<SealedSecret> RewrapAsync(SealedSecret sealedSecret, CancellationToken ct)
    {
        var wrappedDek = Encoding.UTF8.GetString(sealedSecret.WrappedDek);
        // Vault re-wraps the DEK under its latest key version without exposing the DEK.
        var rewrapped = await TransitCallAsync("rewrap", new { ciphertext = wrappedDek }, "ciphertext", ct, refusalIsThisCiphertext: true);
        if (string.Equals(rewrapped, wrappedDek, StringComparison.Ordinal))
        {
            return sealedSecret; // already under the latest key version
        }

        return sealedSecret with { WrappedDek = Encoding.UTF8.GetBytes(rewrapped), KekId = KekId(rewrapped) };
    }

    /// <summary>
    /// Reads the Transit key: its latest version wraps new data keys, and Vault still unwraps every
    /// version from its minimum decryption version up. The token needs <c>read</c> on the key's path.
    /// </summary>
    public async Task<MasterKeys> GetMasterKeysAsync(CancellationToken ct)
    {
        using var key = await TransitSendAsync(HttpMethod.Get, $"v1/{_options.Mount}/keys/{_options.KeyName}", body: null, "keys", refusalIsThisCiphertext: false, ct);
        int latest, oldest;
        try
        {
            var data = key.RootElement.GetProperty("data");
            latest = data.GetProperty("latest_version").GetInt32();
            oldest = Math.Max(1, data.GetProperty("min_decryption_version").GetInt32());
        }
        catch (Exception unreadable) when (unreadable is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new CryptoConfigurationException($"Vault Transit key '{_options.KeyName}' answered an unreadable key description.");
        }

        if (latest < 1 || oldest > latest)
        {
            throw new CryptoConfigurationException(
                $"Vault Transit key '{_options.KeyName}' answered latest version {latest} and minimum decryption version {oldest}.");
        }

        var known = Enumerable.Range(oldest, latest - oldest + 1).Reverse()
            .Select(version => VaultTransitFormat.KekId(_options.KeyName, version))
            .ToList();
        return new MasterKeys(known[0], known, KeyService: true);
    }

    /// <summary>A readable key id for display/grouping: <c>vault:&lt;key&gt;:&lt;version&gt;</c> (e.g. vault:eclipsvault:v1).</summary>
    public string KekId(string vaultCiphertext) => VaultTransitFormat.KekId(_options.KeyName, vaultCiphertext);

    /// <summary>
    /// Calls a Transit operation and returns the named string field of <c>data</c>. Genuinely async:
    /// the HTTP round-trip to Vault is awaited, so a reveal under this engine never blocks a
    /// thread-pool thread — which, on a vault where every reveal wraps or unwraps a DEK, is the whole
    /// reason <see cref="ICryptoEngine"/> is an asynchronous contract.
    /// </summary>
    private async Task<string> TransitCallAsync(
        string operation, object body, string dataField, CancellationToken ct, bool refusalIsThisCiphertext = false)
    {
        using var response = await TransitSendAsync(
            HttpMethod.Post, $"v1/{_options.Mount}/{operation}/{_options.KeyName}", body, operation, refusalIsThisCiphertext, ct);
        return response.RootElement.GetProperty("data").GetProperty(dataField).GetString()
               ?? throw new CryptoConfigurationException($"Vault Transit '{operation}' response missing '{dataField}'.");
    }

    /// <param name="refusalIsThisCiphertext">
    /// For a call about a single ciphertext, a 400 whose errors all blame that ciphertext becomes a
    /// <see cref="CryptographicException"/>. Every other failure, including a 400 about the key such as
    /// "encryption key not found", an unreachable server or a 5xx, means the engine cannot work.
    /// </param>
    private async Task<JsonDocument> TransitSendAsync(
        HttpMethod method, string path, object? body, string operation, bool refusalIsThisCiphertext, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new CryptoConfigurationException($"Vault Transit '{operation}' call failed: {ex.Message}");
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            if (refusalIsThisCiphertext && response.StatusCode == HttpStatusCode.BadRequest && VaultTransitFormat.BlamesTheCiphertext(json))
            {
                throw new CryptographicException($"Vault Transit refused to '{operation}' this ciphertext: {json}");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CryptoConfigurationException($"Vault Transit '{operation}' returned {(int)response.StatusCode}: {json}");
            }

            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                throw new CryptoConfigurationException($"Vault Transit '{operation}' answered a body that is not JSON.");
            }
        }
    }
}

/// <summary>Pure parsing of Vault Transit ciphertext, split out so it can be unit-tested without a server.</summary>
public static class VaultTransitFormat
{
    /// <summary>
    /// Derives a display key id from a Transit ciphertext of the form <c>vault:v&lt;n&gt;:&lt;base64&gt;</c>,
    /// yielding <c>vault:&lt;keyName&gt;:v&lt;n&gt;</c> (so each secret shows which key version wraps it).
    /// </summary>
    public static string KekId(string keyName, string vaultCiphertext)
    {
        var parts = vaultCiphertext.Split(':');
        var version = parts.Length >= 2 && parts[1].Length > 0 ? parts[1] : "v?";
        return $"vault:{keyName}:{version}";
    }

    public static string KekId(string keyName, int version) => $"vault:{keyName}:v{version}";

    /// <summary>
    /// True when every error in a Vault error body is one Vault raises for the ciphertext it was sent:
    /// unparseable, on a version too new or disallowed as too old, or failing authentication.
    /// </summary>
    public static bool BlamesTheCiphertext(string errorBody)
    {
        try
        {
            using var body = JsonDocument.Parse(errorBody);
            var errors = body.RootElement.GetProperty("errors").EnumerateArray().Select(error => error.GetString() ?? string.Empty).ToList();
            return errors.Count > 0 && errors.All(IsCiphertextError);
        }
        catch (Exception unreadable) when (unreadable is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsCiphertextError(string error)
        => error.StartsWith("invalid ciphertext", StringComparison.Ordinal)
           || error == "ciphertext or signature version is disallowed by policy (too old)"
           || error == "cipher: message authentication failed";
}
