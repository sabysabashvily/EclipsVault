namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// One page of the signed-in vault account's own activity, newest first. The paging values and the
/// notable count are Core's, so the Angular UI never re-derives them.
/// </summary>
public sealed record ActivityResponse(
    int Page,
    int PageSize,
    bool HasMore,
    bool HasPrevious,
    int NotableCount,
    IReadOnlyList<ActivityItemResponse> Items);
