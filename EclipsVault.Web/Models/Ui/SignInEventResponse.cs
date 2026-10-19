namespace EclipsVault.Web.Models.Ui;

/// <summary>One sign-in event. The audit row's details text, such as why a password failed, stays on the server.</summary>
public sealed record SignInEventResponse(
    DateTime TimestampUtc,
    [property: UiEnum(typeof(SignInOutcome))] string Outcome,
    [property: UiEnum(typeof(SignInMethod))] string Method,
    string Title,
    string SourceIp,
    [property: UiEnum(typeof(SignInLocationFlag))] string LocationFlag);
