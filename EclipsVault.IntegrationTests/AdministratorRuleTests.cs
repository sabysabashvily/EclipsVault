using System.Net;
using System.Net.Http.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Users endpoints and Legacy forms refuse what Core refuses: changes to the administrator's own account that
/// Profile governs, and values outside the account input rules. A refusal shows its message and
/// changes nothing, not even the audit trail.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class AdministratorRuleTests(VaultFixture vault)
{
    [Fact]
    public async Task Resetting_your_own_MFA_is_refused_with_a_pointer_to_Profile_and_keeps_your_authenticator()
    {
        var administrator = await vault.SignInNewAccountAsync("it-rules-own-mfa", IPAddress.Parse("198.18.107.1"), ClearanceLevel.TopSecret);
        var before = await ProfileHttp.AccountAsync(vault, "it-rules-own-mfa");
        var head = await AuditTrail.HeadAsync(vault);

        var response = await UserActions.ResetMfaAsync(administrator.Client, before.Id);

        await UiApiFailureContractTests.AssertProblemAsync(response, 409, "own_account");
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, head));
        var after = await ProfileHttp.AccountAsync(vault, "it-rules-own-mfa");
        Assert.True(after.TotpEnabled);
        Assert.Equal(before.CredentialGeneration, after.CredentialGeneration);
        Assert.Contains("You cannot reset your own MFA here. Reset it from Profile, which first confirms it is you.",
            (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())!.Title!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabling_your_own_account_is_refused_and_leaves_it_enabled()
    {
        var administrator = await vault.SignInNewAccountAsync("it-rules-own-disable", IPAddress.Parse("198.18.107.2"), ClearanceLevel.TopSecret);
        var account = await ProfileHttp.AccountAsync(vault, "it-rules-own-disable");
        var head = await AuditTrail.HeadAsync(vault);

        var response = await UserActions.SetEnabledAsync(administrator.Client, account.Id, enabled: false);

        await UiApiFailureContractTests.AssertProblemAsync(response, 409, "own_account");
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, head));
        Assert.False((await ProfileHttp.AccountAsync(vault, "it-rules-own-disable")).IsDisabled);
        Assert.Contains("You cannot disable your own account while signed in with it.",
            (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())!.Title!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_your_own_account_is_refused_and_keeps_it()
    {
        var administrator = await vault.SignInNewAccountAsync("it-rules-own-delete", IPAddress.Parse("198.18.107.3"), ClearanceLevel.TopSecret);
        var account = await ProfileHttp.AccountAsync(vault, "it-rules-own-delete");
        var head = await AuditTrail.HeadAsync(vault);

        var response = await UserActions.DeleteAsync(administrator.Client, account.Id);

        await UiApiFailureContractTests.AssertProblemAsync(response, 409, "own_account");
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, head));
        Assert.True(await AccountExistsAsync("it-rules-own-delete"));
        Assert.Contains("You cannot delete your own account while signed in with it.",
            (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())!.Title!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demoting_yourself_while_another_enabled_administrator_exists_is_allowed()
    {
        var administrator = await vault.SignInNewAccountAsync("it-rules-self-demote", IPAddress.Parse("198.18.107.4"), ClearanceLevel.TopSecret);
        var account = await ProfileHttp.AccountAsync(vault, "it-rules-self-demote");

        var response = await LegacyUsers.EditRoleAsync(administrator.Client, account.Id, ClearanceLevel.Secret, "GLOBAL");

        Assert.Equal(LegacyUsers.UsersPage, response.Headers.Location?.OriginalString);
        Assert.Equal(ClearanceLevel.Secret, (await ProfileHttp.AccountAsync(vault, "it-rules-self-demote")).Clearance);
    }

    // MVC's enum binding refuses an undefined clearance before Core sees it. The form's select offers only
    // defined levels, so the page has no place for that message; Core's own rule covers every other caller.
    [Fact]
    public async Task A_new_account_with_an_undefined_clearance_is_refused_by_the_form_and_changes_nothing()
    {
        var admin = await vault.AdminClientAsync();
        var head = await AuditTrail.HeadAsync(vault);

        var response = await admin.PostAsync(LegacyUsers.CreateUserPage, await LegacyUsers.FormAsync(admin, new()
        {
            ["Username"] = "it-rules-clearance-7",
            ["FirstName"] = "Undefined",
            ["LastName"] = "Clearance",
            ["Password"] = "It-rules-initial-password",
            ["Clearance"] = "7",
            ["ProjectKey"] = "GLOBAL",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, head));
        Assert.False(await AccountExistsAsync("it-rules-clearance-7"));
    }

    [Fact]
    public async Task A_role_with_an_undefined_clearance_is_refused_by_the_form_and_keeps_the_role()
    {
        var admin = await vault.AdminClientAsync();
        await LegacyUsers.CreateAsync(admin, "it-rules-role-7");
        var account = await ProfileHttp.AccountAsync(vault, "it-rules-role-7");
        var head = await AuditTrail.HeadAsync(vault);

        var response = await LegacyUsers.EditRoleAsync(admin, account.Id, (ClearanceLevel)7, "OTHER");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, head));
        var stored = await ProfileHttp.AccountAsync(vault, "it-rules-role-7");
        Assert.Equal((ClearanceLevel.Standard, "GLOBAL", account.CredentialGeneration), (stored.Clearance, stored.ProjectKey, stored.CredentialGeneration));
    }

    [Fact]
    public async Task A_name_too_long_for_the_display_name_is_refused_on_the_form()
    {
        var admin = await vault.AdminClientAsync();
        var head = await AuditTrail.HeadAsync(vault);

        var response = await admin.PostAsync(LegacyUsers.CreateUserPage,
            await LegacyUsers.CreateFormAsync(admin, "it-rules-long-name", new string('F', 40), new string('L', 40)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("First and last name together can be at most 64 characters, counting the space between them.",
            await ContentTextAsync(response), StringComparison.Ordinal);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, head));
        Assert.False(await AccountExistsAsync("it-rules-long-name"));
    }

    private static async Task<string> ContentTextAsync(HttpResponseMessage response)
        => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private async Task<bool> AccountExistsAsync(string username)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.Users.AnyAsync(user => user.Username == username);
    }
}
