using System.Net.Http.Json;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.IntegrationTests;

internal static class UserAccounts
{
    public const string UsersPage = "/Admin/Users";
    public const string CreatePath = "/api/ui/users";

    /// <summary>The mailbox a helper-created account is invited at, so a test can read its invitation.</summary>
    public static string EmailOf(string username) => $"{username.Trim()}@example.test";

    public static Task<HttpResponseMessage> CreateAsync(HttpClient admin, string username,
        string firstName = "Integration", string lastName = "Test", string? email = null,
        ClearanceLevel clearance = ClearanceLevel.Standard, string project = "GLOBAL")
        => CreateAsync(admin, CreateContent(username, firstName, lastName, email, clearance, project));

    public static async Task<HttpResponseMessage> CreateAsync(HttpClient admin, HttpContent content)
        => await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), HttpMethod.Post, CreatePath, content);

    public static JsonContent CreateContent(string username, string firstName, string lastName, string? email = null,
        ClearanceLevel clearance = ClearanceLevel.Standard, string project = "GLOBAL")
        => JsonContent.Create(new
        {
            username,
            firstName,
            lastName,
            email = email ?? EmailOf(username),
            clearance = (int)clearance,
            project
        });

    public static async Task<HttpResponseMessage> EditRoleAsync(HttpClient admin, Guid accountId, ClearanceLevel clearance, string project)
        => await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), HttpMethod.Patch,
            $"/api/ui/users/{accountId}/role", JsonContent.Create(new { clearance = (int)clearance, project }));
}
