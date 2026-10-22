namespace EclipsVault.Core.Domain;

/// <summary>
/// How the vault compares email addresses, which are sign-in and single sign-on identities: one
/// address is one identity however it is spelled, on either database. An account keeps the spelling
/// it was given for display and delivery.
/// </summary>
public static class EmailIdentity
{
    /// <summary>
    /// Invariant casing keeps identity independent of the database's locale. Only characters of the
    /// Basic Multilingual Plane change case: SQL Server translates single UTF-16 units, so both
    /// databases can reproduce exactly this. Dots and plus tags stay significant: whether they
    /// matter is each mail provider's rule, not the vault's.
    /// </summary>
    public static string Normalize(string email)
    {
        var folded = email.Trim().ToCharArray();
        for (var i = 0; i < folded.Length; i++)
        {
            folded[i] = char.ToUpperInvariant(folded[i]);
        }

        return new string(folded);
    }
}
