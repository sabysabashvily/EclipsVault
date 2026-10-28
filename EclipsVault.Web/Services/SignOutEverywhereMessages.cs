namespace EclipsVault.Web.Services;

/// <summary>What the account is told after signing out everywhere, on the Legacy sign-in page and in the UI API.</summary>
public static class SignOutEverywhereMessages
{
    public static string For(SignOutEverywhereOutcome outcome) => outcome switch
    {
        SignOutEverywhereOutcome.SignedOut => "You have been signed out of all sessions. Sign in again to continue.",
        SignOutEverywhereOutcome.RevokedUnrecorded =>
            "You have been signed out of all sessions, but the vault couldn't record it in the audit trail. Tell your administrator.",
        _ => "This browser was signed out, but the vault couldn't confirm that your other devices were. Sign in and try again.",
    };
}
