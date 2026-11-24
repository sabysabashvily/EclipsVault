using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EclipsVault.Core.Application.DynamicSecrets;

/// <summary>
/// Builds a PostgreSQL-compatible RFC 7677 <c>SCRAM-SHA-256</c> password verifier on the client so
/// <c>CREATE ROLE ... PASSWORD '...'</c> never transmits or logs the plaintext credential on the target server.
/// </summary>
public static class ScramSha256Verifier
{
    public const int DefaultIterations = 4096;
    private const int SaltLengthBytes = 16;
    private const int KeyLengthBytes = 32;

    private static ReadOnlySpan<byte> ClientKeyLabel => "Client Key"u8;
    private static ReadOnlySpan<byte> ServerKeyLabel => "Server Key"u8;

    /// <summary>
    /// Computes a freshly salted <c>SCRAM-SHA-256</c> verifier for <paramref name="password"/>.
    /// Requires a strictly ASCII alphanumeric password (<see cref="CredentialStatementTemplate.IsRenderablePassword"/>),
    /// for which RFC 4013 SASLprep normalisation is the identity function.
    /// </summary>
    public static string Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        try
        {
            return Create(password, salt, DefaultIterations);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public static string Create(string password, ReadOnlySpan<byte> salt, int iterations = DefaultIterations)
    {
        if (!CredentialStatementTemplate.IsRenderablePassword(password))
        {
            // Never echo the rejected value: it may be a live credential.
            throw new ArgumentException(
                "A SCRAM-SHA-256 dynamic credential password must be non-empty and strictly alphanumeric.",
                nameof(password));
        }

        if (salt.IsEmpty)
        {
            throw new ArgumentException("Salt must not be empty.", nameof(salt));
        }

        if (iterations < DefaultIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), $"SCRAM-SHA-256 requires at least {DefaultIterations} iterations.");
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var saltedPassword = new byte[KeyLengthBytes];
        var clientKey = new byte[KeyLengthBytes];
        var storedKey = new byte[KeyLengthBytes];
        var serverKey = new byte[KeyLengthBytes];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, saltedPassword, iterations, HashAlgorithmName.SHA256);
            HMACSHA256.HashData(saltedPassword, ClientKeyLabel, clientKey);
            SHA256.HashData(clientKey, storedKey);
            HMACSHA256.HashData(saltedPassword, ServerKeyLabel, serverKey);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"SCRAM-SHA-256${iterations}:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(saltedPassword);
            CryptographicOperations.ZeroMemory(clientKey);
            CryptographicOperations.ZeroMemory(storedKey);
            CryptographicOperations.ZeroMemory(serverKey);
        }
    }
}
