using System.Globalization;
using System.Security.Cryptography;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.IntegrationTests;

/// <summary>The Legacy Users pages' forms, posted the way an administrator's browser posts them.</summary>
internal static class LegacyUsers
{
    public const string UsersPage = "/Admin/Users";

    public const string CreateUserPage = "/Admin/CreateUser";

    public static async Task<HttpResponseMessage> CreateAsync(HttpClient admin, string username)
        => await admin.PostAsync(CreateUserPage, await CreateFormAsync(admin, username, "Integration", "Test"));

    /// <summary>A filled-in New user form for a Standard account, ready to post.</summary>
    public static Task<FormUrlEncodedContent> CreateFormAsync(HttpClient admin, string username, string firstName, string lastName)
        => FormAsync(admin, new()
        {
            ["Username"] = username,
            ["FirstName"] = firstName,
            ["LastName"] = lastName,
            ["Password"] = "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            ["Clearance"] = ((int)ClearanceLevel.Standard).ToString(CultureInfo.InvariantCulture),
            ["ProjectKey"] = "GLOBAL",
        });

    public static async Task<HttpResponseMessage> EditRoleAsync(HttpClient admin, Guid accountId, ClearanceLevel clearance, string project)
        => await admin.PostAsync($"/Admin/EditUser/{accountId}", await FormAsync(admin, new()
        {
            ["Id"] = accountId.ToString(),
            ["Clearance"] = ((int)clearance).ToString(CultureInfo.InvariantCulture),
            ["ProjectKey"] = project,
        }));

    public static async Task<HttpResponseMessage> SetEnabledAsync(HttpClient admin, Guid accountId, bool enabled)
        => await admin.PostAsync($"/Admin/SetEnabled/{accountId}", await FormAsync(admin, new()
        {
            ["enabled"] = enabled ? "true" : "false",
        }));

    public static async Task<HttpResponseMessage> UnlockAsync(HttpClient admin, Guid accountId)
        => await admin.PostAsync($"/Admin/Unlock/{accountId}", await FormAsync(admin, new()));

    public static async Task<HttpResponseMessage> ForceLogoutAsync(HttpClient admin, Guid accountId)
        => await admin.PostAsync($"/Admin/ForceLogout/{accountId}", await FormAsync(admin, new()));

    public static async Task<HttpResponseMessage> DeleteAsync(HttpClient admin, Guid accountId)
        => await admin.PostAsync($"/Admin/DeleteUser/{accountId}", await FormAsync(admin, new()));

    /// <summary>The form's fields with the antiforgery token the Users page renders for this session.</summary>
    public static async Task<FormUrlEncodedContent> FormAsync(HttpClient admin, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = VaultFixture.FormTokenOf(await admin.GetStringAsync(UsersPage), UsersPage);
        return new FormUrlEncodedContent(fields);
    }
}
