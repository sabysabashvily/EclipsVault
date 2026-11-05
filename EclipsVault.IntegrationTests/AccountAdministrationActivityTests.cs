using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Models.Ui;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class AccountAdministrationActivityTests(VaultFixture vault)
{
    [Fact]
    public async Task User_administration_appears_only_in_the_acting_administrators_activity()
    {
        var administrator = await vault.SignInNewAccountAsync("it-user-activity-admin", IPAddress.Parse("198.19.112.1"), ClearanceLevel.TopSecret);
        var observer = await vault.SignInNewAccountAsync("it-user-activity-observer", IPAddress.Parse("198.19.112.2"));
        var response = await UserAccounts.CreateAsync(administrator.Client, "it-user-activity-target");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await response.Content.ReadFromJsonAsync<CreatedUserResponse>();
        Assert.NotNull(account);
        Assert.Equal(HttpStatusCode.NoContent, (await UserAccounts.EditRoleAsync(administrator.Client, account.UserId, ClearanceLevel.Secret, "ACTIVITY")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UserActions.ResetMfaAsync(administrator.Client, account.UserId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UserActions.SetEnabledAsync(administrator.Client, account.UserId, false)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UserActions.SetEnabledAsync(administrator.Client, account.UserId, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UserActions.ForceLogoutAsync(administrator.Client, account.UserId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UserActions.DeleteAsync(administrator.Client, account.UserId)).StatusCode);

        Assert.Equal(["Deleted a user", "Forced a user to sign out", "Enabled a user", "Disabled a user", "Reset a user's MFA", "Changed a user's role", "Created a user"],
            await AdministrationTitlesAsync(administrator.Client));
        Assert.Empty(await AdministrationTitlesAsync(observer.Client));
    }

    [Fact]
    public async Task Service_account_administration_appears_only_in_the_acting_administrators_activity()
    {
        var administrator = await vault.SignInNewAccountAsync("it-sa-activity-admin", IPAddress.Parse("198.19.112.3"), ClearanceLevel.TopSecret);
        var observer = await vault.SignInNewAccountAsync("it-sa-activity-observer", IPAddress.Parse("198.19.112.4"));
        var account = await ServiceAccountsApi.CreatedAsync(vault, administrator.Client, "it-sa-activity-target");
        var key = await ServiceAccountsApi.IssuedAsync(administrator.Client, account.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await ServiceAccountsApi.RevokeKeyAsync(administrator.Client, account.Id, key.KeyId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ServiceAccountsApi.SetEnabledAsync(administrator.Client, account.Id, false)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ServiceAccountsApi.SetEnabledAsync(administrator.Client, account.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ServiceAccountsApi.DeleteAsync(administrator.Client, account.Id)).StatusCode);

        Assert.Equal(["Deleted a service account", "Enabled a service account", "Disabled a service account", "Revoked an API key", "Issued an API key", "Created a service account"],
            await AdministrationTitlesAsync(administrator.Client));
        Assert.Empty(await AdministrationTitlesAsync(observer.Client));
    }

    private static async Task<string[]> AdministrationTitlesAsync(HttpClient client)
    {
        using var activity = JsonDocument.Parse(await client.GetStringAsync("/api/ui/activity"));
        return activity.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("category").GetString() == "Administration")
            .Select(item => item.GetProperty("title").GetString()!)
            .ToArray();
    }
}
