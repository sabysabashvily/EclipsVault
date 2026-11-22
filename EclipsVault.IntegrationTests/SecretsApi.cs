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

    /// <summary>
    /// An unknown secret's 404 <c>not_found</c>, identical to a decoy's in status, headers, content type and body, so
    /// the answer cannot tell which ids are decoys.
    /// </summary>
    public static async Task AssertSameAnswerAsync(HttpResponseMessage unknown, HttpResponseMessage decoy)
    {
        await AssertProblemAsync(unknown, 404, "not_found");
        Assert.Equal(decoy.StatusCode, unknown.StatusCode);
        Assert.Equal(HeaderNames(decoy), HeaderNames(unknown));
        Assert.Equal(decoy.Content.Headers.ContentType, unknown.Content.Headers.ContentType);
        Assert.Equal(await decoy.Content.ReadAsByteArrayAsync(), await unknown.Content.ReadAsByteArrayAsync());
    }

    private static IEnumerable<string> HeaderNames(HttpResponseMessage response)
        => response.Headers.Concat(response.Content.Headers).Select(header => header.Key).Where(key => key != "Date").Order();

    private static readonly string[] ProblemMembers = ["code", "detail", "errors", "proof", "reasons", "status", "title", "type"];
}
