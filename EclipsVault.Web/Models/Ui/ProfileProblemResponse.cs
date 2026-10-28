using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Models.Ui;

/// <summary>A refused Profile request. <see cref="Errors"/> names the fields at fault, never what was entered.</summary>
public sealed class ProfileProblemResponse : ProblemDetails
{
    public required string Code { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}
