using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Models.Ui;

/// <summary>A refused access-request decision. <see cref="Errors"/> names the field at fault, never what was sent.</summary>
public sealed class AccessRequestProblemResponse : ProblemDetails
{
    public required string Code { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}
