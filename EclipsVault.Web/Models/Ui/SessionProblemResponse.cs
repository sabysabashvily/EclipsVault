using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Models.Ui;

public sealed class SessionProblemResponse : ProblemDetails
{
    public required string Code { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SignedOut { get; init; }
}
