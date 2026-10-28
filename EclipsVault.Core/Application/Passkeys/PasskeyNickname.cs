namespace EclipsVault.Core.Application.Passkeys;

public static class PasskeyNickname
{
    public const int MaxLength = 64;

    /// <summary>The name a passkey is stored under: trimmed, "Passkey" when blank, cut to <see cref="MaxLength"/>.</summary>
    public static string Normalize(string? nickname)
    {
        nickname = nickname?.Trim();
        if (string.IsNullOrEmpty(nickname))
        {
            return "Passkey";
        }

        return nickname.Length > MaxLength ? nickname[..MaxLength] : nickname;
    }
}
