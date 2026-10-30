namespace EclipsVault.Core.Domain;

/// <summary>
/// How the vault compares email addresses, which are sign-in and single sign-on identities: one
/// address is one identity however it is spelled, on either database. An account keeps the spelling
/// it was given for display and delivery.
/// </summary>
public static class EmailIdentity
{
    /// <summary>
    /// Only the letters a to z ignore case. Unicode casing maps other letters onto them ('ı' and 'ſ'
    /// become 'I' and 'S'), so a provider could vouch for a different mailbox, "admın@…", and single
    /// sign-on would match the account for "admin@…". Dots and plus tags stay significant: whether
    /// they matter is each mail provider's rule, not the vault's.
    /// </summary>
    public static string Normalize(string email)
    {
        var folded = email.Trim().ToCharArray();
        for (var i = 0; i < folded.Length; i++)
        {
            if (char.IsAsciiLetterLower(folded[i]))
            {
                folded[i] = char.ToUpperInvariant(folded[i]);
            }
        }

        return new string(folded);
    }
}
