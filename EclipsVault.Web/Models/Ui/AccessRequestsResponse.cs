using System.ComponentModel.DataAnnotations;

namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// The Access requests page. <see cref="ReviewScope"/> is <c>all</c> for an administrator and <c>project</c>
/// for anyone else; either way <see cref="ToReview"/> holds only the requests the caller could decide now.
/// </summary>
public sealed record AccessRequestsResponse(
    [property: AllowedValues("all", "project")] string ReviewScope,
    IReadOnlyList<RequestToReviewResponse> ToReview,
    IReadOnlyList<FiledRequestResponse> Mine);
