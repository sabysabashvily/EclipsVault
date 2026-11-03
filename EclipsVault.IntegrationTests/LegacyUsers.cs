using System.Globalization;
using System.Security.Cryptography;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.IntegrationTests;

/// <summary>The Legacy New user and Edit user forms, posted as an administrator's browser does.</summary>
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

    /// <summary>The form's fields with the antiforgery token the New user page renders for this session.</summary>
    public static async Task<FormUrlEncodedContent> FormAsync(HttpClient admin, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = VaultFixture.FormTokenOf(await admin.GetStringAsync(CreateUserPage), CreateUserPage);
        return new FormUrlEncodedContent(fields);
    }
}
