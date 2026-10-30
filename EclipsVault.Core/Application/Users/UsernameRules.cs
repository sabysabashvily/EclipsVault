using System.Text.RegularExpressions;

namespace EclipsVault.Core.Application.Users;

/// <summary>
/// The usernames a new account may have, shared by Core and the administrator's form. Without '@',
/// a username can never be mistaken for another account's email address at sign-in.
/// </summary>
public static partial class UsernameRules
{
    public const int MinimumLength = 3;

    public const int MaximumLength = 64;

    public const string AllowedCharactersPattern = @"^[a-zA-Z0-9_\-\.]+$";

    public const string Description = "Use letters, digits, underscores, hyphens or dots.";

    /// <summary>
    /// Checks the whole name, as the form's validation does: <c>$</c> alone would also accept a name
    /// followed by a line break.
    /// </summary>
    public static bool Allows(string username)
        => username.Length is >= MinimumLength and <= MaximumLength
           && Allowed().Match(username) is { Success: true } match
           && match.Length == username.Length;

    [GeneratedRegex(AllowedCharactersPattern)]
    private static partial Regex Allowed();
}
