using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace EclipsVault.Tests.Fakes;

/// <summary>
/// A Vault Transit server for one aes256-gcm96 key, in process. It answers the routes the engine calls
/// (read key, encrypt, decrypt, rewrap) with Vault's paths, token header, bodies and error shapes, and
/// really encrypts under each key version, so the real <see cref="VaultTransitCryptoEngine"/> runs
/// against it unchanged. Like Vault, a rewrap always re-encrypts, even a ciphertext already on the
/// latest version.
/// </summary>
public sealed class FakeVaultTransit : HttpMessageHandler
{
    public const string KeyName = "eclipsvault";

    private const string Token = "fake-transit-token";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly List<byte[]> _versions = [RandomNumberGenerator.GetBytes(32)];

    public int LatestVersion => _versions.Count;

    /// <summary>Vault refuses to decrypt or rewrap a ciphertext on an older version than this.</summary>
    public int MinDecryptionVersion { get; set; } = 1;

    public int Rewraps { get; private set; }

    /// <summary>Once this many rewraps have been answered, every rewrap fails with <see cref="OutageAnswer"/>.</summary>
    public int? RewrapsBeforeOutage { get; set; }

    /// <summary>The status and error Vault answers during the outage; none means it refuses connections.</summary>
    public (HttpStatusCode Status, string Error)? OutageAnswer { get; set; }

    /// <summary>Once this many rewraps have been answered, the key is rotated once, as an operator might mid-pass.</summary>
    public int? RewrapsBeforeKeyRotation { get; set; }

    /// <summary>A raw body to answer the key read with, in place of the key's description.</summary>
    public string? KeyDescription { get; set; }

    public void RotateKey() => _versions.Add(RandomNumberGenerator.GetBytes(32));

    public VaultTransitCryptoEngine Engine() => new(
        new HttpClient(this),
        Options.Create(new VaultOptions
        {
            Address = "http://vault.test:8200",
            KeyName = KeyName,
            // Names nothing sets, so the engine takes the development token and never the host's VAULT_TOKEN.
            TokenEnvironmentVariable = "ECLIPSVAULT_TEST_UNSET_VAULT_TOKEN",
            DevelopmentToken = Token,
        }),
        Options.Create(new CryptoOptions()));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var isRewrap = path == PathOf("rewrap");
        if (isRewrap && Rewraps >= RewrapsBeforeOutage)
        {
            return OutageAnswer is { } answer
                ? Errors(answer.Status, answer.Error)
                : throw new HttpRequestException("Connection refused (vault.test:8200)");
        }

        if (isRewrap && Rewraps == RewrapsBeforeKeyRotation)
        {
            RotateKey();
        }

        if (!request.Headers.TryGetValues("X-Vault-Token", out var tokens) || tokens.Single() != Token)
        {
            return Errors(HttpStatusCode.Forbidden, "permission denied");
        }

        var body = request.Content is null
            ? default
            : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement;

        if (request.Method == HttpMethod.Get && path == PathOf("keys") && KeyDescription is not null)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(KeyDescription) };
        }

        if (request.Method == HttpMethod.Get && path == PathOf("keys"))
        {
            return Data(new
            {
                name = KeyName,
                type = "aes256-gcm96",
                latest_version = LatestVersion,
                min_decryption_version = MinDecryptionVersion,
                min_encryption_version = 0,
            });
        }

        if (request.Method != HttpMethod.Post)
        {
            return Errors(HttpStatusCode.MethodNotAllowed, "unsupported operation");
        }

        if (path == PathOf("encrypt"))
        {
            return Data(new { ciphertext = Encrypt(Convert.FromBase64String(body.GetProperty("plaintext").GetString()!)), key_version = LatestVersion });
        }

        if (path == PathOf("decrypt"))
        {
            return Decrypt(body.GetProperty("ciphertext").GetString()!, out var plaintext) is { } refused
                ? refused
                : Data(new { plaintext = Convert.ToBase64String(plaintext) });
        }

        if (isRewrap)
        {
            Rewraps++;
            return Decrypt(body.GetProperty("ciphertext").GetString()!, out var plaintext) is { } refused
                ? refused
                : Data(new { ciphertext = Encrypt(plaintext), key_version = LatestVersion });
        }

        return Errors(HttpStatusCode.NotFound, $"no handler for route \"{path}\"");
    }

    private string Encrypt(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var sealedBytes = new byte[plaintext.Length + TagSize];
        using var gcm = new AesGcm(_versions[^1], TagSize);
        gcm.Encrypt(nonce, plaintext, sealedBytes.AsSpan(0, plaintext.Length), sealedBytes.AsSpan(plaintext.Length));
        return $"vault:v{LatestVersion}:{Convert.ToBase64String([.. nonce, .. sealedBytes])}";
    }

    /// <returns>Vault's 400 when it cannot open the ciphertext; otherwise null, with the plaintext.</returns>
    private HttpResponseMessage? Decrypt(string ciphertext, out byte[] plaintext)
    {
        plaintext = [];
        var parts = ciphertext.Split(':');
        if (parts.Length != 3 || parts[0] != "vault")
        {
            return Errors(HttpStatusCode.BadRequest, "invalid ciphertext: no prefix");
        }

        if (!int.TryParse(parts[1].TrimStart('v'), out var version))
        {
            return Errors(HttpStatusCode.BadRequest, "invalid ciphertext: version number could not be decoded");
        }

        if (version > LatestVersion)
        {
            return Errors(HttpStatusCode.BadRequest, "invalid ciphertext: version is too new");
        }

        if (version < MinDecryptionVersion)
        {
            return Errors(HttpStatusCode.BadRequest, "ciphertext or signature version is disallowed by policy (too old)");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return Errors(HttpStatusCode.BadRequest, "invalid ciphertext: could not decode base64");
        }

        if (bytes.Length < NonceSize + TagSize)
        {
            return Errors(HttpStatusCode.BadRequest, "invalid ciphertext length");
        }

        try
        {
            var sealedLength = bytes.Length - NonceSize - TagSize;
            plaintext = new byte[sealedLength];
            using var gcm = new AesGcm(_versions[version - 1], TagSize);
            gcm.Decrypt(bytes.AsSpan(0, NonceSize), bytes.AsSpan(NonceSize, sealedLength), bytes.AsSpan(NonceSize + sealedLength), plaintext);
            return null;
        }
        catch (CryptographicException)
        {
            return Errors(HttpStatusCode.BadRequest, "cipher: message authentication failed");
        }
    }

    private static string PathOf(string operation) => $"/v1/transit/{operation}/{KeyName}";

    private static HttpResponseMessage Data(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { data }) };

    private static HttpResponseMessage Errors(HttpStatusCode status, string error) => new(status) { Content = JsonContent.Create(new { errors = new[] { error } }) };
}
