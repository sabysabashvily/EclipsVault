using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

internal static class SecretsApi
{
    public const string Path = "/api/ui/secrets";

    public static JsonContent CreateContent(string name, string value = "it-secret-value", string project = "GLOBAL",
        string environment = "Development", string sensitivity = "Internal", int ttlDays = 0)
        => JsonContent.Create(new
        {
            name,
            value,
            project,
            environment,
            sensitivity = (int)Enum.Parse<SensitivityLevel>(sensitivity),
            ttlDays
        });

    /// <summary>Creates a secret the way New secret does. A success answers 201 with the secret's details page as its location.</summary>
    public static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string name, string value = "it-secret-value",
        string project = "GLOBAL", string environment = "Development", string sensitivity = "Internal", int ttlDays = 0)
        => await UiApiRequest.SendWithTokenAsync(client, await UiApiRequest.AntiforgeryTokenAsync(client), HttpMethod.Post, Path,
            CreateContent(name, value, project, environment, sensitivity, ttlDays));

    /// <summary>A refused write's problem, which may carry the policy's <c>reasons</c> and nothing else beyond the usual members.</summary>
    public static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "The response is not no-store");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(problem.EnumerateObject(), member => Assert.Contains(member.Name, ProblemMembers));
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    private static readonly string[] ProblemMembers = ["code", "detail", "errors", "proof", "reasons", "status", "title", "type"];
}
