using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace EclipsVault.Core.Application.Users;

/// <summary>
/// Cryptographic token generation and hashing for account invitations (ADR-0033).
/// Tokens carry 256 random bits encoded as Base64Url; only their SHA-256 hash is stored.
/// </summary>
public static class InvitationToken
{
    private const int EntropyBytes = 32;

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(EntropyBytes));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static bool Matches(string token, byte[] expectedHash)
        => !string.IsNullOrEmpty(token) && CryptographicOperations.FixedTimeEquals(Hash(token), expectedHash);
}
