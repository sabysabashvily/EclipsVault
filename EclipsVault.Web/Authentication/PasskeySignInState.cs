using System.Text.Json;

namespace EclipsVault.Web.Authentication;

public sealed record PasskeySignInState(string Challenge, string? ReturnUrl, PasskeyNamedAccount? NamedAccount)
{
    public static PasskeySignInState? Read(string? stored)
    {
        try
        {
            return stored is null ? null : JsonSerializer.Deserialize<PasskeySignInState>(stored);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
