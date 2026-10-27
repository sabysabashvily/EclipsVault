using System.Buffers.Binary;
using System.Security.Cryptography;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// RFC 6238 authenticator codes, written independently of the vault's own implementation so a test
/// signs in the way an authenticator app does rather than by asking the vault what it expects.
/// </summary>
internal static class Totp
{
    private const int StepSeconds = 30;

    public static long CurrentStep() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / StepSeconds;

    public static int SecondsLeftInStep() => StepSeconds - (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % StepSeconds);

    /// <summary>A code for the next step, so it is newer than any code the sign-in already spent.</summary>
    public static string NextCode(string base32Secret) => Code(base32Secret, CurrentStep() + 1);

    public static string Code(string base32Secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(Base32Decode(base32Secret), counter);

        var offset = hash[^1] & 0x0f;
        var binary = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in secret.TrimEnd('='))
        {
            buffer = ((buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal)) & 0xffff;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
            }
        }

        return [.. bytes];
    }
}
