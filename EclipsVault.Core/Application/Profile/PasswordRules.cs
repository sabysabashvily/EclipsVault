namespace EclipsVault.Core.Application.Profile;

/// <summary>The rules every password a person chooses must keep: Profile's change and account activation.</summary>
public static class PasswordRules
{
    public const int MinimumLength = 12;

    public const int MaximumLength = 256;

    /// <summary>Why <paramref name="password"/> breaks the rules, or null when it keeps them.</summary>
    public static string? Refusal(string? password, IBreachedPasswordScreen breachScreen)
    {
        if (password is null || password.Length < MinimumLength)
        {
            return $"Your new password must be at least {MinimumLength} characters long.";
        }

        if (password.Length > MaximumLength)
        {
            return $"Your new password must be at most {MaximumLength} characters long.";
        }

        return breachScreen.IsCompromised(password)
            ? "This password has appeared in a known data breach. Choose a different one."
            : null;
    }
}
