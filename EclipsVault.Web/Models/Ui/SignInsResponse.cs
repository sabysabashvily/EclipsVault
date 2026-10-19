namespace EclipsVault.Web.Models.Ui;

/// <summary>The signed-in vault account's recent sign-in events, newest first, and Core's summary of them.</summary>
public sealed record SignInsResponse(
    SignInSummaryResponse Summary,
    IReadOnlyList<SignInEventResponse> Events);
