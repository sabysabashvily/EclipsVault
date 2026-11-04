using System.Net.Http.Json;
using System.Security.Cryptography;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.IntegrationTests;

internal static class UserAccounts
{
    public const string UsersPage = "/Admin/Users";
    public const string CreatePath = "/api/ui/users";

    public static Task<HttpResponseMessage> CreateAsync(HttpClient admin, string username,
        string firstName = "Integration", string lastName = "Test", string? password = null,
        ClearanceLevel clearance = ClearanceLevel.Standard, string project = "GLOBAL")
        => CreateAsync(admin, CreateContent(username, firstName, lastName, password, clearance, project));

    public static async Task<HttpResponseMessage> CreateAsync(HttpClient admin, HttpContent content)
        => await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), HttpMethod.Post, CreatePath, content);

    public static JsonContent CreateContent(string username, string firstName, string lastName, string? password = null,
        ClearanceLevel clearance = ClearanceLevel.Standard, string project = "GLOBAL")
        => JsonContent.Create(new
        {
            username,
            firstName,
            lastName,
            password = password ?? "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            clearance = (int)clearance,
            project
        });

    public static async Task<HttpResponseMessage> EditRoleAsync(HttpClient admin, Guid accountId, ClearanceLevel clearance, string project)
        => await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), HttpMethod.Patch,
            $"/api/ui/users/{accountId}/role", JsonContent.Create(new { clearance = (int)clearance, project }));
}
