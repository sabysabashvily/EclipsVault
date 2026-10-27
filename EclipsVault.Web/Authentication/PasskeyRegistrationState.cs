using System.Security.Claims;
using System.Text.Json;
using EclipsVault.Web.Extensions;

namespace EclipsVault.Web.Authentication;

public sealed record PasskeyRegistrationState(string Challenge, Guid AccountId, Guid SessionId, SessionStamp Stamp)
{
    public bool BelongsTo(ClaimsPrincipal principal)
        => principal.GetUserIdOrNull() == AccountId && principal.GetSessionId() == SessionId
            && principal.GetSessionStampOrNull() == Stamp;

    public static PasskeyRegistrationState? Read(string? stored)
    {
        try
        {
            return stored is null ? null : JsonSerializer.Deserialize<PasskeyRegistrationState>(stored);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
