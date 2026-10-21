using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Models.Ui;

public sealed class ShareProblemResponse : ProblemDetails
{
    public required string Code { get; init; }
}
