namespace EclipsVault.Web.Models.Ui;

/// <summary>The "at a glance" counts over the same events the history lists.</summary>
public sealed record SignInSummaryResponse(
    int SuccessCount,
    int FailedCount,
    int SuspiciousCount,
    int DistinctLocations,
    DateTime? LastSuccessUtc,
    DateTime? LastFailedUtc,
    bool NeedsAttention);
