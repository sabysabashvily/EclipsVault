namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// The signed-in vault account's security checkup. The counts and the next step are Core's;
/// <c>NextStep</c> is null when every control passes.
/// </summary>
public sealed record SecurityResponse(
    int Score,
    [property: UiEnum(typeof(SecurityGrade))] string Grade,
    int ActionNeededCount,
    int RecommendedCount,
    int PassCount,
    string? NextStep,
    IReadOnlyList<SecurityCheckResponse> Checks);
